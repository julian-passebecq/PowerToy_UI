using System.Text.Json;
using System.Text.Json.Serialization;

namespace JUtility.Core.Files;

public sealed class FileTrayFolder
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>Remembered destination for "Move to project folder" (a Repository Hub project has no local path of its own).</summary>
public sealed class FileTrayProjectFolder
{
    public Guid ProjectId { get; set; }
    public string Path { get; set; } = string.Empty;
}

public sealed record WatchedFolder(string Path, string Label);

// Separate bounded file (file-tray.json): business schema v7 and shell schema 1 are unchanged. Settings only:
// which folders to watch and how many files to keep. No file names, contents or thumbnails are ever written.
public sealed class FileTraySettings
{
    public const string FormatName = "powerops-file-tray";
    public const int MaxExtraFolders = 8, MaxProjectFolders = 200, MaxPathLength = 1024, MaxLabelLength = 64;

    [JsonRequired]
    public string Format { get; set; } = FormatName;
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    // Off = no watcher, no scan, no timer. On does not start anything by itself: watching starts the first time a
    // tray surface is shown or a tray action runs in this session.
    public bool Enabled { get; set; } = true;
    public bool WatchDownloads { get; set; } = true;
    public List<FileTrayFolder> ExtraFolders { get; set; } = [];
    public int MaxItems { get; set; } = FileTrayList.DefaultCapacity;
    public List<FileTrayProjectFolder> ProjectFolders { get; set; } = [];

    public static void Validate(FileTraySettings settings)
    {
        if (settings is null || settings.Format != FormatName || settings.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported file-tray format/version. Existing files were not rewritten.");
        FileTrayList.CheckCapacity(settings.MaxItems);
        if (settings.ExtraFolders is null || settings.ExtraFolders.Count > MaxExtraFolders)
            throw new InvalidDataException($"The tray watches at most {MaxExtraFolders} extra folders.");
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FileTrayFolder? folder in settings.ExtraFolders)
        {
            if (folder is null) throw new InvalidDataException("Invalid watched folder.");
            if (!folders.Add(NormalizeFolder(folder.Path))) throw new InvalidDataException($"{folder.Path} is listed twice.");
            if (folder.Label is null || folder.Label.Length > MaxLabelLength) throw new InvalidDataException("Folder labels are at most 64 characters.");
        }

        if (settings.ProjectFolders is null || settings.ProjectFolders.Count > MaxProjectFolders)
            throw new InvalidDataException("Invalid project folder collection.");
        var projects = new HashSet<Guid>();
        foreach (FileTrayProjectFolder? link in settings.ProjectFolders)
        {
            if (link is null || link.ProjectId == Guid.Empty || !projects.Add(link.ProjectId))
                throw new InvalidDataException("Missing or duplicate project folder.");
            NormalizeFolder(link.Path);
        }
    }

    /// <summary>Absolute local folder, without a trailing separator (except a drive root). Rejects relative and URL-like paths.</summary>
    public static string NormalizeFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength || path.Contains("://", StringComparison.Ordinal))
            throw new InvalidDataException("Choose an existing local folder.");
        string trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed)) throw new InvalidDataException($"'{trimmed}' is not a full folder path.");
        string full = Path.GetFullPath(trimmed);
        string root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    /// <summary>Downloads (when enabled) then the extra folders, each folder once.</summary>
    public IReadOnlyList<WatchedFolder> ResolveFolders(string? downloadsFolder)
    {
        var result = new List<WatchedFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (WatchDownloads && !string.IsNullOrWhiteSpace(downloadsFolder))
        {
            string downloads = NormalizeFolder(downloadsFolder);
            seen.Add(downloads);
            result.Add(new WatchedFolder(downloads, "Downloads"));
        }

        foreach (FileTrayFolder folder in ExtraFolders)
        {
            string path = NormalizeFolder(folder.Path);
            if (!seen.Add(path)) continue;
            string label = string.IsNullOrWhiteSpace(folder.Label) ? Path.GetFileName(path) : folder.Label.Trim();
            result.Add(new WatchedFolder(path, label.Length == 0 ? path : label));
        }

        return result.AsReadOnly();
    }

    public string? ProjectFolder(Guid projectId) => ProjectFolders.FirstOrDefault(x => x.ProjectId == projectId)?.Path;

    public void RememberProjectFolder(Guid projectId, string path)
    {
        string folder = NormalizeFolder(path);
        FileTrayProjectFolder? existing = ProjectFolders.FirstOrDefault(x => x.ProjectId == projectId);
        if (existing is not null)
        {
            existing.Path = folder;
            return;
        }

        if (ProjectFolders.Count >= MaxProjectFolders) ProjectFolders.RemoveAt(0);
        ProjectFolders.Add(new FileTrayProjectFolder { ProjectId = projectId, Path = folder });
    }
}

public sealed class FileTraySettingsStore
{
    public const int MaxBytes = 64 * 1024;
    public string FilePath { get; }

    public FileTraySettingsStore(string directory) =>
        FilePath = Path.Combine(Path.GetFullPath(directory), "file-tray.json");

    public static FileTraySettings Copy(FileTraySettings settings) =>
        JsonSerializer.Deserialize<FileTraySettings>(JsonSerializer.SerializeToUtf8Bytes(settings, Json), Json)!;

    /// <summary>Missing file = defaults (nothing written). Malformed/future files fail closed and are preserved.</summary>
    public FileTraySettings Load() => File.Exists(FilePath) ? Read(FilePath) : new FileTraySettings();

    public static FileTraySettings Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("File tray JSON exceeds 64 KiB.");
        var settings = JsonSerializer.Deserialize<FileTraySettings>(stream, Json) ?? throw new InvalidDataException("Empty file tray JSON.");
        FileTraySettings.Validate(settings);
        return settings;
    }

    public void Save(FileTraySettings settings)
    {
        FileTraySettings.Validate(settings);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("File tray JSON exceeds 64 KiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(FilePath)) _ = Read(FilePath); // Never overwrite unsupported/corrupt bytes.
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".backup", true);
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() },
    };
}
