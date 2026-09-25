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
