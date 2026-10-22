using System.IO;
using System.Text;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace JUtility.App.Services;

// The only type that touches Windows Runtime APIs, so Microsoft.Windows.SDK.NET/WinRT.Runtime load on the first
// "Copy text" (image or scanned PDF) or "Copy as image" (PDF), never at startup. Windows OCR runs offline with the
// recognizers of the user's Windows display languages; nothing is uploaded.
internal static class FileTrayWinRt
{
    public const int MaxOcrPages = 10;

    public static async Task<string> OcrImageAsync(string path, CancellationToken cancellation)
    {
        OcrEngine engine = Engine();
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        using SoftwareBitmap bitmap = await DecodeForOcr(decoder);
        cancellation.ThrowIfCancellationRequested();
        return Lines(await engine.RecognizeAsync(bitmap));
    }

    /// <summary>OCR of the first <see cref="MaxOcrPages"/> pages, each rendered at about 200 dpi.</summary>
    public static async Task<string> OcrPdfAsync(string path, CancellationToken cancellation)
    {
        OcrEngine engine = Engine();
        PdfDocument document = await Load(path);
        uint pages = Math.Min(document.PageCount, MaxOcrPages);
        var text = new StringBuilder();
        for (uint index = 0; index < pages; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            using PdfPage page = document.GetPage(index);
            uint width = (uint)Math.Clamp(page.Size.Width * 200 / 96, 64, OcrEngine.MaxImageDimension);
            using var rendered = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions { DestinationWidth = width });
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(rendered);
            using SoftwareBitmap bitmap = await DecodeForOcr(decoder);
            string pageText = Lines(await engine.RecognizeAsync(bitmap));
            if (pageText.Length == 0) continue;
            if (text.Length > 0) text.Append("\n\n");
            text.Append(pageText);
        }

        if (document.PageCount > pages) text.Append($"\n\n[... OCR stopped after {pages} of {document.PageCount} pages]");
        return text.ToString();
    }

    public static async Task<byte[]> RenderPdfPagePngAsync(string path, uint pageIndex, uint width)
    {
        PdfDocument document = await Load(path);
        if (document.PageCount <= pageIndex) throw new InvalidDataException("This PDF has no page to show.");
        using PdfPage page = document.GetPage(pageIndex);
        using var rendered = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions { DestinationWidth = width, BitmapEncoderId = BitmapEncoder.PngEncoderId });
        return await ReadAll(rendered);
    }

    /// <summary>For images WPF cannot decode (for example HEIC with the Windows HEIF extension): PNG through Windows imaging.</summary>
    public static async Task<byte[]> ConvertImageToPngAsync(string path, uint maxSide)
    {
        using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        using SoftwareBitmap bitmap = await Decode(decoder, maxSide);
        using var output = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        return await ReadAll(output);
    }

    private static OcrEngine Engine() => OcrEngine.TryCreateFromUserProfileLanguages()
        ?? throw new InvalidOperationException(
            "Windows OCR has no text recognizer for your Windows languages. Add a language with OCR in Settings > Time & language > Language & region.");

    private static async Task<PdfDocument> Load(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        try
        {
            return await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x8007052B)) // ERROR_WRONG_PASSWORD
        {
            throw new InvalidOperationException("This PDF is password-protected.", ex);
        }
    }

    private static Task<SoftwareBitmap> DecodeForOcr(BitmapDecoder decoder) => Decode(decoder, OcrEngine.MaxImageDimension);

    private static async Task<SoftwareBitmap> Decode(BitmapDecoder decoder, uint maxSide)
    {
        var transform = new BitmapTransform();
        uint longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        if (longest > maxSide)
        {
            double scale = (double)maxSide / longest;
            transform.ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale));
            transform.ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale));
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
    }

    private static string Lines(OcrResult result) => string.Join("\n", result.Lines.Select(line => line.Text));

    private static async Task<byte[]> ReadAll(IRandomAccessStream stream)
    {
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
