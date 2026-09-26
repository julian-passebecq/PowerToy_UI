using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JUtility.App.Services;

// File tray plumbing that only needs Win32/WPF: Explorer thumbnails, file and image clipboard formats, drag-out.
internal static class FileTrayShell
{
    public const int MaxImageSide = 4096;

    /// <summary>
    /// Explorer's own thumbnail, or the file-type icon when no thumbnail handler is installed (for example PDFs without
    /// a PDF previewer). Explorer keeps its thumbnail cache; the caller keeps the small bitmap only while the file is in
    /// the tray. Call on an STA thread. Returns a frozen bitmap, or null.
    /// </summary>
    public static BitmapSource? Thumbnail(string path, int size)
    {
        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            if (factory.GetImage(new NativeSize { Width = size, Height = size }, 0 /* SIIGBF_RESIZETOFIT */, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            return ToBitmapSource(bitmap);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException or UnauthorizedAccessException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>CF_HDROP with one file, plus "Preferred DropEffect = Copy" so pasting in Explorer copies instead of moving it.</summary>
    public static DataObject FileDropData(string path)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
        return data;
    }

    public static void CopyFile(string path) => SetClipboard(FileDropData(path));

    /// <summary>
    /// Exactly two formats: "PNG" (what Chrome and Edge paste, with transparency) and one 32-bit CF_DIB for other apps.
    /// DataObject.SetImage would publish several full-size bitmap formats, which the process keeps while they are on
    /// the clipboard (measured: +370 MB for one rendered PDF page).
    /// </summary>
    public static void CopyImage(BitmapSource image, byte[] png)
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(png), autoConvert: false);
        data.SetData(DataFormats.Dib, DibStream(image), autoConvert: false);
        SetClipboard(data);
    }

    /// <summary>BITMAPINFOHEADER + bottom-up 32 bpp BI_RGB rows.</summary>
    private static MemoryStream DibStream(BitmapSource image)
    {
        BitmapSource bgra = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        byte[] pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        var stream = new MemoryStream(40 + pixels.Length);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)32);
            writer.Write(0); writer.Write(pixels.Length); writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
            for (int row = height - 1; row >= 0; row--) writer.Write(pixels, row * stride, stride);
        }

        stream.Position = 0;
        return stream;
    }

    public static DragDropEffects DragFile(DependencyObject source, string path) =>
        DragDrop.DoDragDrop(source, FileDropData(path), DragDropEffects.Copy);

    /// <summary>First frame, scaled down so the longest side is at most <see cref="MaxImageSide"/>. Frozen; safe off the UI thread.</summary>
    public static BitmapSource LoadImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return Fit(decoder.Frames[0]);
    }

    public static BitmapSource DecodePng(byte[] png)
    {
        using var stream = new MemoryStream(png);
        BitmapSource frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource Fit(BitmapSource frame)
    {
        int longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        BitmapSource result = longest <= MaxImageSide
            ? frame
            : new TransformedBitmap(frame, new ScaleTransform((double)MaxImageSide / longest, (double)MaxImageSide / longest));
        if (result.Format != PixelFormats.Bgra32 && result.Format != PixelFormats.Pbgra32 && result.Format != PixelFormats.Bgr32)
            result = new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0);
        var copy = new WriteableBitmap(result); // materializes the pixels so the file can close
        copy.Freeze();
        return copy;
    }

    private static void SetClipboard(DataObject data)
    {
        // A clipboard manager can hold the clipboard for a moment; retry briefly instead of failing the action.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static BitmapSource? ToBitmapSource(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out NativeBitmap bm) == 0 || bm.Width <= 0 || bm.Height == 0) return null;
        int width = bm.Width, height = Math.Abs(bm.Height);
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32, Compression = 0,
            },
        };
        byte[] pixels = new byte[width * height * 4];
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(screen, hbitmap, 0, (uint)height, pixels, ref info, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        // Icons come with alpha (premultiplied); photo thumbnails often have an all-zero alpha byte.
        bool alpha = false;
        for (int i = 3; i < pixels.Length && !alpha; i += 4) alpha = pixels[i] != 0;
        var source = BitmapSource.Create(width, height, 96, 96, alpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfo info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
