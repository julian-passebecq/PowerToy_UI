using System.Reflection;

namespace PowerRing.Core;

/// <summary>
/// ring.json in its folder, plus ring.schema.json and RING_CONFIG.md written next to it (always refreshed, they are ours)
/// so VS Code and any AI assistant can edit the file with the exact list of fields, actions and icon names.
/// </summary>
public sealed class RingConfigStore
{
    public const string FileName = "ring.json", SchemaFileName = "ring.schema.json", GuideFileName = "RING_CONFIG.md";
    public const int MaxBytes = 512 * 1024;

    public RingConfigStore(string configPath) => FilePath = Path.GetFullPath(configPath);

    public string FilePath { get; }
    public string Directory => Path.GetDirectoryName(FilePath)!;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerRing", FileName);

    /// <summary>Creates the folder and a default ring.json when missing; never overwrites the user's ring.json.</summary>
    public void EnsureFiles()
    {
        System.IO.Directory.CreateDirectory(Directory);
        if (!File.Exists(FilePath)) File.WriteAllText(FilePath, RingDefaults.Json);
        WriteIfChanged(Path.Combine(Directory, SchemaFileName), Resource(SchemaFileName));
        WriteIfChanged(Path.Combine(Directory, GuideFileName), Resource(GuideFileName));
    }

    public RingConfig Load()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists) throw new RingConfigException($"{FilePath} does not exist.");
        if (info.Length > MaxBytes) throw new RingConfigException("ring.json is larger than 512 KB.");
        // Editors save in several steps; a short retry avoids reading a half-written or locked file.
        for (int attempt = 1; ; attempt++)
        {
            try { return RingConfigs.Parse(File.ReadAllText(FilePath)); }
            catch (IOException) when (attempt < 5) { Thread.Sleep(80); }
        }
    }

    /// <summary>
    /// Writes ring.json from the tray menu (enable/disable a workspace, add a preset). The previous file is kept as
    /// ring.json.bak first, because re-writing drops the comments of a hand-edited file.
    /// </summary>
    public void Save(RingConfig config)
    {
        RingConfigs.Validate(config);
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, RingConfigs.Serialize(config));
        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>Alternative ring.json files to try (tray menu > Layouts): %APPDATA%\PowerRing\layouts\*.json.</summary>
    public string LayoutsDirectory => Path.Combine(Directory, "layouts");

    public IReadOnlyList<string> Layouts() =>
        System.IO.Directory.Exists(LayoutsDirectory)
            ? System.IO.Directory.GetFiles(LayoutsDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>Makes a layout the active ring.json (validated first; the current file is kept as ring.json.bak).</summary>
    public void ApplyLayout(string layoutPath)
    {
        string text = File.ReadAllText(layoutPath).Replace("\"../ring.schema.json\"", "\"./ring.schema.json\"");
        RingConfigs.Parse(text);
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, FilePath, overwrite: true);
    }

    public static string Resource(string name)
    {
        using Stream stream = typeof(RingConfigStore).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WriteIfChanged(string path, string text)
    {
        try
        {
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
        }
        catch (IOException) { } // a locked helper file is not worth failing the ring over
    }
}
