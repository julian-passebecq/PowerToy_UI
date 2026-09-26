using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PowerRing;

/// <summary>
/// The site's own icon for web buttons (GitHub, ChatGPT, Gmail...). Asked once from that site only (apple-touch-icon,
/// then favicon.ico), cached as a file in %APPDATA%\PowerRing\icons, and shown from the cache afterwards. Until it
/// arrives (or if the site has none) the glyph is used. Turn off with "webIcons": false.
/// </summary>
internal static class WebIcons
{
    private static readonly Dictionary<string, ImageSource?> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static HttpClient? _http;
    private static string? _folder;
    private static Dispatcher? _dispatcher;

    /// <summary>Raised on the UI thread when a new icon has been cached, so an open ring can redraw.</summary>
    public static event EventHandler? Arrived;

    public static void Initialize(string settingsFolder, Dispatcher dispatcher)
    {
        _folder = Path.Combine(settingsFolder, "icons");
        _dispatcher = dispatcher;
    }

    public static ImageSource? For(string? url)
    {
        if (_folder is null || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return null;
        string host = uri.Host.ToLowerInvariant();
        if (Loaded.TryGetValue(host, out ImageSource? image)) return image;
        string file = Path.Combine(_folder, host + ".png");
        if (File.Exists(file))
        {
            image = Decode(File.ReadAllBytes(file));
            Loaded[host] = image;
            return image;
        }
        if (File.Exists(file + ".none")) { Loaded[host] = null; return null; }
        if (Pending.Add(host)) _ = Fetch(uri, host, file);
        return null;
    }

    private static async Task Fetch(Uri uri, string host, string file)
    {
        _http ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(6) };
        var origin = new Uri($"{uri.Scheme}://{uri.Authority}/");
        byte[]? best = null;
        foreach (string path in new[] { "/apple-touch-icon.png", "/favicon.ico" })
        {
            best = await TryImage(new Uri(origin, path));
            if (best is not null) break;
        }
        // Sites without the usual files declare their icon in the page: <link rel="icon" href="...">.
        if (best is null)
        {
            foreach (Uri declared in await DeclaredIcons(uri))
            {
                best = await TryImage(declared);
                if (best is not null) break;
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (best is null) { File.WriteAllText(file + ".none", "no icon"); return; }
            // Stored as PNG whatever the source format, so the cache reads the same way every time.
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)Decode(best)!));
            using (var stream = File.Create(file)) encoder.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        _dispatcher?.BeginInvoke(() =>
        {
            Loaded.Remove(host);
            Arrived?.Invoke(null, EventArgs.Empty);
        });
    }

    private static async Task<byte[]?> TryImage(Uri address)
    {
        try
        {
            using HttpResponseMessage response = await _http!.GetAsync(address);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 512 * 1024) return null;
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length is > 64 and < 512 * 1024 && Decode(bytes) is not null ? bytes : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException) { return null; }
    }

    /// <summary>Icon links declared by the page (largest "sizes" first), PNG/ICO only (SVG is not drawn here).</summary>
    private static async Task<IReadOnlyList<Uri>> DeclaredIcons(Uri page)
    {
        try
        {
            using HttpResponseMessage response = await _http!.GetAsync(page, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return [];
            await using Stream stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[256 * 1024];
            int read = 0, n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read))) > 0) read += n;
            string html = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            var links = new List<(Uri Address, int Size)>();
            foreach (System.Text.RegularExpressions.Match tag in System.Text.RegularExpressions.Regex.Matches(html, "<link\\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                string text = tag.Value;
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, "rel\\s*=\\s*[\"'][^\"']*icon", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var href = System.Text.RegularExpressions.Regex.Match(text, "href\\s*=\\s*[\"']([^\"']+)[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!href.Success || href.Groups[1].Value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || href.Groups[1].Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Uri.TryCreate(response.RequestMessage?.RequestUri ?? page, System.Net.WebUtility.HtmlDecode(href.Groups[1].Value), out Uri? address) || address.Scheme is not ("http" or "https")) continue;
                var sizes = System.Text.RegularExpressions.Regex.Match(text, "sizes\\s*=\\s*[\"'](\\d+)x", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                links.Add((address, sizes.Success && int.TryParse(sizes.Groups[1].Value, out int size) ? size : 32));
            }
            return links.OrderByDescending(x => x.Size).Select(x => x.Address).Take(4).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException) { return []; }
    }

    /// <summary>The largest frame of an .ico or the image itself; null if it is not an image.</summary>
    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            BitmapDecoder decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException) { return null; }
    }
}
