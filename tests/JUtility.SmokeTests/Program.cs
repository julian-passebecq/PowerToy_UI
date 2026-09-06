using JUtility.Core.Models;
using JUtility.Core.Services;

List<string> failures = [];

Check("project formatting obeys field toggles", () =>
{
    ProjectEntry project = new()
    {
        Name = "Demo",
        RepoUrl = "https://github.com/example/demo",
        SiteUrl = "https://demo.example/",
        ExtraUrl = "https://extra.example/",
        CopyName = true,
        CopyRepo = true,
        CopySite = false,
        CopyExtra = true,
    };

    string actual = ProjectClipboardFormatter.FormatRow(project);
    Equal("Demo https://github.com/example/demo https://extra.example/", actual);
});

Check("copy all excludes disabled and archived rows", () =>
{
    ProjectEntry included = new() { Name = "A", CopyName = true, IncludeInCopyAll = true };
    ProjectEntry disabled = new() { Name = "B", CopyName = true, IncludeInCopyAll = false };
    ProjectEntry archived = new() { Name = "C", CopyName = true, IncludeInCopyAll = true, IsArchived = true };
    Equal("A", ProjectClipboardFormatter.FormatAll([included, disabled, archived]));
});

Check("URL normalizer accepts bare domains and rejects non-web schemes", () =>
{
    True(UrlNormalizer.TryNormalizeOptionalWebUrl("example.com/path", out string normalized));
    Equal("https://example.com/path", normalized.TrimEnd('/'));
    False(UrlNormalizer.TryNormalizeOptionalWebUrl("file:///c:/temp/test.txt", out _));
});

Check("prompt composer orders modules and resolves project variables", () =>
{
    PromptModuleEntry second = new() { Title = "Second", SortOrder = 20, Body = "Repo={{repo}}" };
    PromptModuleEntry first = new() { Title = "First", SortOrder = 10, Body = "Project {{project}}" };
    ProjectEntry project = new() { Name = "Demo", RepoUrl = "https://github.com/example/demo" };
    string text = PromptComposer.Compose([second, first], project);
    Equal("Project Demo" + Environment.NewLine + Environment.NewLine + "Repo=https://github.com/example/demo", text);
});

Check("workspace store round-trips and creates backup", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilitySmoke-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        True(state.Projects.Count >= 2);
        state.Projects.Add(new ProjectEntry { Name = "RoundTrip" });
        store.Save(state);
        WorkspaceState loaded = store.Load();
        True(loaded.Projects.Any(project => project.Name == "RoundTrip"));
        True(File.Exists(store.BackupFilePath));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("v1 always-on-top preference migrates to window behavior mode", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityMigration-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "workspace.json"),
            "{\"SchemaVersion\":1,\"Preferences\":{\"AlwaysOnTop\":true},\"Projects\":[],\"PromptModules\":[],\"RecentPrompts\":[],\"Notes\":[]}");

        WorkspaceStore store = new(root);
        WorkspaceState loaded = store.Load();
        True(loaded.SchemaVersion == WorkspaceState.CurrentSchemaVersion);
        True(loaded.Preferences.WindowBehavior == WindowBehaviorMode.AlwaysOnTop);
        True(loaded.Preferences.AlwaysOnTop);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("summon preferences have safe defaults", () =>
{
    AppPreferences preferences = new();
    True(preferences.WindowBehavior == WindowBehaviorMode.Normal);
    True(preferences.SummonMouseBinding == SummonMouseBinding.MouseButton5);
    True(preferences.HideOnFocusLoss);
    True(preferences.OpenNearCursor);
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} smoke test(s) failed:");
    foreach (string failure in failures)
    {
        Console.Error.WriteLine("- " + failure);
    }
    Environment.Exit(1);
}

Console.WriteLine("All J Utility smoke tests passed.");

void Check(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS: " + name);
    }
    catch (Exception ex)
    {
        failures.Add(name + " - " + ex.Message);
    }
}

static void Equal(string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected}' but got '{actual}'.");
    }
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

static void False(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected false.");
    }
}
