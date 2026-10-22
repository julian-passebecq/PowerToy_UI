using System.Globalization;

namespace JUtility.Core.Files;

// V2.3 File tray: recently received files (Downloads and user-chosen folders such as the WhatsApp save folder)
// ready to drop or paste into an AI chat. The tray holds paths and file metadata only; it never stores or
// exports file contents, and nothing here reads a file unless the user asks for its text or image.

public enum FileTrayKind
{
    Pdf,
    Image,
    Document,
    Text,
}

/// <param name="ArrivedUtc">When the file became ready in a watched folder (or its newest file time for files found by a scan).</param>
public sealed record FileTrayEntry(
    string Path,
    string SourceFolder,
    string SourceLabel,
    FileTrayKind Kind,
    long Length,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset ArrivedUtc)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Key => FileTrayFilter.Key(Path);
}

public static class FileTrayFilter
{
    public static IReadOnlyList<string> Extensions { get; } =
        Array.AsReadOnly(new[] { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".heic", ".docx", ".txt" });

    // Chrome/Edge (.crdownload), Firefox (.part), Opera (.opdownload), Safari-style (.download) and generic temp files.
    private static readonly string[] PartialExtensions = [".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".crswap"];

    public static FileTrayKind? KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => FileTrayKind.Pdf,
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".heic" => FileTrayKind.Image,
        ".docx" => FileTrayKind.Document,
        ".txt" => FileTrayKind.Text,
        _ => null,
    };

    /// <summary>Unfinished downloads and editor lock files. They are never shown; the finished file arrives by rename or write.</summary>
    public static bool IsPartial(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Length == 0) return true;
        if (name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith(".~lock.", StringComparison.Ordinal)) return true;
        string extension = Path.GetExtension(name);
        return PartialExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Name-only check (no disk access), cheap enough to run on every watcher event.</summary>
    public static bool IsCandidate(string path) => !IsPartial(path) && KindOf(path) is not null;

    public static bool IsCandidate(FileSystemInfo info) =>
        IsCandidate(info.FullName)
        && (info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory | FileAttributes.Temporary)) == 0;

    /// <summary>Identity used for dedupe: full path, case-insensitive like the Windows file system.</summary>
    public static string Key(string path) => Path.GetFullPath(path).ToUpperInvariant();
}

/// <summary>
/// The ordered, bounded tray: newest arrival first, one entry per file, at most <see cref="Capacity"/> entries.
/// Not thread-safe; <see cref="FileTrayService"/> guards it.
/// </summary>
public sealed class FileTrayList
{
    public const int MinCapacity = 1, MaxCapacity = 100, DefaultCapacity = 20;

    private readonly List<FileTrayEntry> _items = [];
    private readonly HashSet<string> _dismissed = new(StringComparer.Ordinal);

    public FileTrayList(int capacity = DefaultCapacity, IEnumerable<string>? dismissed = null)
    {
        Capacity = CheckCapacity(capacity);
        if (dismissed is not null) _dismissed.UnionWith(dismissed);
    }

    public int Capacity { get; private set; }

    public IReadOnlyList<FileTrayEntry> Items => _items.ToArray();

    public FileTrayEntry? Latest => _items.Count == 0 ? null : _items[0];

    /// <summary>Session-only: files removed from the tray. Carried over when the watcher is rebuilt, never saved.</summary>
    public IReadOnlyCollection<string> DismissedKeys => _dismissed.ToArray();

    public static int CheckCapacity(int capacity) => capacity is >= MinCapacity and <= MaxCapacity
        ? capacity
        : throw new InvalidDataException($"The tray keeps between {MinCapacity} and {MaxCapacity} files.");

    public static IComparer<FileTrayEntry> Order { get; } = Comparer<FileTrayEntry>.Create((a, b) =>
    {
        int result = b.ArrivedUtc.CompareTo(a.ArrivedUtc);
        if (result == 0) result = b.LastWriteUtc.CompareTo(a.LastWriteUtc);
        if (result == 0) result = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        return result == 0 ? StringComparer.Ordinal.Compare(a.Key, b.Key) : result;
    });

    /// <summary>
    /// Adds or refreshes one file. The same file seen again (another event, another watched folder, different path
    /// casing) stays a single entry; if its size and write time are unchanged it keeps its place. True when the tray changed.
    /// </summary>
    public bool Upsert(FileTrayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!FileTrayFilter.IsCandidate(entry.Path) || entry.Length <= 0 || _dismissed.Contains(DismissKey(entry))) return false;
        string key = entry.Key;
        int index = _items.FindIndex(x => x.Key == key);
        if (index >= 0)
        {
            FileTrayEntry existing = _items[index];
            if (existing.Length == entry.Length && existing.LastWriteUtc == entry.LastWriteUtc) return false;
            _items.RemoveAt(index);
        }

        _items.Add(entry);
        _items.Sort(Order);
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        return index >= 0 || _items.Contains(entry);
    }

    public bool Remove(string path)
    {
        string key = FileTrayFilter.Key(path);
        return _items.RemoveAll(x => x.Key == key) > 0;
    }

    /// <summary>Removes the file from the tray for this session. It comes back only if it is written again.</summary>
    public bool Dismiss(string path)
    {
        string key = FileTrayFilter.Key(path);
        FileTrayEntry? entry = _items.FirstOrDefault(x => x.Key == key);
        if (entry is null) return false;
        _dismissed.Add(DismissKey(entry));
        return Remove(path);
    }

    /// <summary>Keeps a moved file in the tray at its new location and in its current position.</summary>
    public bool Relocate(string oldPath, string newPath, string sourceLabel)
    {
        string key = FileTrayFilter.Key(oldPath);
        int index = _items.FindIndex(x => x.Key == key);
        if (index < 0) return false;
        FileTrayEntry moved = _items[index] with
        {
            Path = Path.GetFullPath(newPath),
            SourceFolder = Path.GetDirectoryName(Path.GetFullPath(newPath)) ?? string.Empty,
            SourceLabel = sourceLabel,
        };
        _items.RemoveAt(index);
        string newKey = moved.Key;
        _items.RemoveAll(x => x.Key == newKey);
        _items.Add(moved);
        _items.Sort(Order);
        return true;
    }

    public bool SetCapacity(int capacity)
    {
        Capacity = CheckCapacity(capacity);
        if (_items.Count <= Capacity) return false;
        _items.RemoveRange(Capacity, _items.Count - Capacity);
        return true;
    }

    /// <summary>Drops entries whose file is gone (for example after a watcher buffer overflow). True when the tray changed.</summary>
    public bool Prune(Func<string, bool> exists) => _items.RemoveAll(x => !exists(x.Path)) > 0;

    private static string DismissKey(FileTrayEntry entry) =>
        entry.Key + "|" + entry.LastWriteUtc.UtcTicks.ToString(CultureInfo.InvariantCulture) + "|" + entry.Length.ToString(CultureInfo.InvariantCulture);
}

public enum FileReadiness
{
    Ready,
    NotCandidate,
    Missing,
    Empty,
    Locked,
}

public static class FileTrayReadiness
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Ready only when the file exists, is not empty and no other process has it open for writing (a download or
    /// app still saving it). The probe opens the file for a moment without reading it.
    /// </summary>
    public static FileReadiness Probe(string path)
    {
        if (!FileTrayFilter.IsCandidate(path)) return FileReadiness.NotCandidate;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return FileReadiness.Missing;
            if (!FileTrayFilter.IsCandidate(info)) return FileReadiness.NotCandidate;
            if (info.Length == 0) return FileReadiness.Empty; // e.g. Firefox's placeholder before the .part rename
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return FileReadiness.Ready;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return FileReadiness.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileReadiness.Locked;
        }
    }

    /// <summary>
    /// One-shot re-check delays while a file is still being written: 0.25 s doubling to 8 s, then every 8 s, for about
    /// two minutes in total. Null means give up; a later rename or write event starts again. Nothing runs when nothing is pending.
    /// </summary>
    public static TimeSpan? RetryDelay(int attempt)
    {
        if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt));
        double elapsed = 0;
        for (int i = 0; i <= attempt; i++)
        {
            double delay = Math.Min(8000, 250 * Math.Pow(2, Math.Min(i, 5)));
            if (elapsed + delay > GiveUpAfter.TotalMilliseconds) return null;
            if (i == attempt) return TimeSpan.FromMilliseconds(delay);
            elapsed += delay;
        }

        return null;
    }
}

public static class FileTrayFormat
{
    public static string Age(DateTimeOffset now, DateTimeOffset then)
    {
        TimeSpan span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        if (span < TimeSpan.FromDays(2)) return "yesterday";
        if (span < TimeSpan.FromDays(14)) return $"{(int)span.TotalDays} days ago";
        return then.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB"),
    };

    public static string KindLabel(FileTrayKind kind) => kind switch
    {
        FileTrayKind.Pdf => "PDF",
        FileTrayKind.Image => "Image",
        FileTrayKind.Document => "Word",
        _ => "Text",
    };
}
