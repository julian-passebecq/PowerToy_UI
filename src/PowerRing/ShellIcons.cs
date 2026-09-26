using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PowerRing.Core;

namespace PowerRing;

/// <summary>
/// The real icons Windows shows for a program, folder or file (IShellItemImageFactory, with alpha), so the ring looks
/// like the desktop: VS Code's own icon, the Downloads folder icon... Cached per path; misses fall back to a glyph.
/// </summary>
internal static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>appearance.webIcons: web buttons show the site's own icon.</summary>
    public static bool WebIconsEnabled { get; set; } = true;

    /// <summary>Icon image for an item: its icon file/path, else what its target points to. Null = use a glyph.</summary>
    public static ImageSource? For(RingItem item)
    {
        // An icon file always wins. A web button prefers the site's own icon; its icon name is the fallback glyph.
        if (item.Icon is string icon && RingIcons.IsFile(icon)) return Load(Environment.ExpandEnvironmentVariables(icon));
        if (RingActions.Of(item) == RingActions.Url) return WebIconsEnabled ? WebIcons.For(item.Target) : null;
        if (item.Icon is not null) return null;
        string? path = RingActions.Of(item) switch
        {
            RingActions.Run => ResolveProgram(item.Target),
            RingActions.Folder => ActionRunner.ResolveFolder(Environment.ExpandEnvironmentVariables(item.Target ?? "")),
            RingActions.PowerOps when !string.IsNullOrWhiteSpace(item.Target) => Environment.ExpandEnvironmentVariables(item.Target),
            _ => null,
        };
        return path is null ? null : Load(path);
    }

    public static ImageSource? Load(string path)
    {
        if (Cache.TryGetValue(path, out ImageSource? cached)) return cached;
        ImageSource? image = null;
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                image = extension is ".png" or ".jpg" or ".jpeg" ? Picture(path) : Shell(path, 64);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or COMException or InvalidCastException) { }
        Cache[path] = image;
        return image;
    }

    /// <summary>A command like "code", "wt.exe" or "services.msc" resolved to the file Windows would start.</summary>
    public static string? ResolveProgram(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        string expanded = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (Path.IsPathRooted(expanded)) return File.Exists(expanded) ? expanded : null;
        string name = Path.HasExtension(expanded) ? expanded : expanded + ".exe";
        foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using RegistryKey? key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{name}");
            if (key?.GetValue(null) is string registered && File.Exists(registered.Trim('"'))) return registered.Trim('"');
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                     .Prepend(Environment.SystemDirectory))
        {
            string candidate = Path.Combine(folder.Trim(), name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static ImageSource Picture(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.DecodePixelWidth = 64;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? Shell(string path, int size)
    {
        Guid iid = typeof(IShellItemImageFactory).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItemImageFactory factory);
        try
        {
            // SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK: the icon, never a document thumbnail.
            if (factory.GetImage(new NativeSize { Width = size, Height = size }, 0x4 | 0x1, out IntPtr bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            try { return FromHBitmap(bitmap); }
            finally { Native.DeleteObject(bitmap); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    /// <summary>32-bit DIB with pre-multiplied alpha to a frozen WPF image (CreateBitmapSourceFromHBitmap would drop the alpha).</summary>
    private static BitmapSource? FromHBitmap(IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<BitmapInfoHeaderSource>(), out BitmapInfoHeaderSource info) == 0 || info.BitsPixel != 32) return null;
        int width = info.Width, height = Math.Abs(info.Height), stride = width * 4;
        var pixels = new byte[stride * height];
        var header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        IntPtr dc = Native.GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, dc); }
        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        image.Freeze();
        return image;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeaderSource { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height; public ushort Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr gdiObject, int size, out BitmapInfoHeaderSource info);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
}
