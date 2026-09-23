using System.IO;
using System.Windows.Media.Imaging;
using JUtility.Core.Models;

namespace JUtility.App.Services;

public sealed class ClipboardMediaStorageService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".m4v",
    };

    private readonly string _dataDirectory;
    private readonly string _mediaDirectory;

    public ClipboardMediaStorageService(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _mediaDirectory = Path.Combine(_dataDirectory, "media");
        Directory.CreateDirectory(_mediaDirectory);
    }

    public ClipboardMediaEntry SaveClipboardImage(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        Guid id = Guid.NewGuid();
        string fileName = $"{id:N}.png";
        string destination = Path.Combine(_mediaDirectory, fileName);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (FileStream stream = File.Create(destination))
        {
            encoder.Save(stream);
        }

        FileInfo info = new(destination);
        return CreateEntry(id, ClipboardMediaKind.Image, destination, info.Length, "image/png", "Screenshot");
    }

    public ClipboardMediaEntry ImportFile(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The selected media file does not exist.", source);
        }

        string extension = Path.GetExtension(source);
        ClipboardMediaKind kind;
        if (ImageExtensions.Contains(extension))
        {
            kind = ClipboardMediaKind.Image;
        }
        else if (VideoExtensions.Contains(extension))
        {
            kind = ClipboardMediaKind.Video;
        }
        else
        {
            throw new InvalidDataException($"Unsupported media type '{extension}'.");
        }

        Guid id = Guid.NewGuid();
        string fileName = $"{id:N}{extension.ToLowerInvariant()}";
        string destination = Path.Combine(_mediaDirectory, fileName);
        File.Copy(source, destination, overwrite: false);

        FileInfo info = new(destination);
        string mimeType = GuessMimeType(extension, kind);
        string title = Path.GetFileNameWithoutExtension(source);
        return CreateEntry(id, kind, destination, info.Length, mimeType, title);
    }

    public void ResolvePath(ClipboardMediaEntry media)
    {
        ArgumentNullException.ThrowIfNull(media);
        media.ResolvedPath = ResolveRelativePath(media.RelativePath);
    }

    public string ResolveRelativePath(string relativePath)
    {
        string normalized = (relativePath ?? string.Empty).Trim().Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.None);
        if (normalized.Length == 0
            || Path.IsPathRooted(normalized)
            || normalized.Contains(':', StringComparison.Ordinal)
            || segments.Length < 2
            || !segments[0].Equals("media", StringComparison.OrdinalIgnoreCase)
            || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException("Media path must stay under the managed media/ directory.");
        }

        string combined = Path.GetFullPath(Path.Combine(_dataDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string mediaRoot = Path.GetFullPath(_mediaDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Media path escapes the managed media directory.");
        }

        return combined;
    }

    public void DeleteManagedFile(ClipboardMediaEntry media)
    {
        ArgumentNullException.ThrowIfNull(media);
        string path = ResolveRelativePath(media.RelativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        media.ResolvedPath = string.Empty;
    }

    private ClipboardMediaEntry CreateEntry(
        Guid id,
        ClipboardMediaKind kind,
        string destination,
        long byteLength,
        string mimeType,
        string title)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ClipboardMediaEntry
        {
            Id = id,
            Kind = kind,
            Title = string.IsNullOrWhiteSpace(title) ? (kind == ClipboardMediaKind.Video ? "Clip" : "Image") : title.Trim(),
            Category = "Personal",
            FileName = Path.GetFileName(destination),
            RelativePath = Path.GetRelativePath(_dataDirectory, destination).Replace('\\', '/'),
            ResolvedPath = destination,
            MimeType = mimeType,
            ByteLength = byteLength,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
    }

    private static string GuessMimeType(string extension, ClipboardMediaKind kind) =>
        extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            _ => kind == ClipboardMediaKind.Video ? "video/*" : "image/*",
        };
}
