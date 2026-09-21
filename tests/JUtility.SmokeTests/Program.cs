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

Check("repository rows support server and ChatGPT links", () =>
{
    ProjectEntry project = new()
    {
        Name = "Power Ops",
        RepoUrl = "https://github.com/example/powerops",
        SiteUrl = "https://powerops.example/",
        ServerUrl = "https://app.netlify.com/sites/powerops",
        ChatGptUrl = "https://chatgpt.com/c/example",
        CopyName = false,
        CopyRepo = true,
        CopySite = true,
        CopyServer = true,
        CopyChatGpt = true,
    };

    Equal(
        "https://github.com/example/powerops https://powerops.example/ https://app.netlify.com/sites/powerops https://chatgpt.com/c/example",
        ProjectClipboardFormatter.FormatRow(project));
});

Check("copy all emits URLs only and excludes disabled and archived rows", () =>
{
    ProjectEntry included = new()
    {
        Name = "A",
        RepoUrl = "https://github.com/example/a",
        SiteUrl = "https://a.example/",
        CopyName = true,
        CopyRepo = true,
        CopySite = true,
        IncludeInCopyAll = true,
    };
    ProjectEntry disabled = new() { Name = "B", RepoUrl = "https://github.com/example/b", CopyRepo = true, IncludeInCopyAll = false };
    ProjectEntry archived = new() { Name = "C", RepoUrl = "https://github.com/example/c", CopyRepo = true, IncludeInCopyAll = true, IsArchived = true };
    Equal("https://github.com/example/a https://a.example/", ProjectClipboardFormatter.FormatAll([included, disabled, archived]));
});

Check("URL normalizer accepts bare domains and rejects non-web schemes", () =>
{
    True(UrlNormalizer.TryNormalizeOptionalWebUrl("example.com/path", out string normalized));
    Equal("https://example.com/path", normalized.TrimEnd('/'));
    False(UrlNormalizer.TryNormalizeOptionalWebUrl("file:///c:/temp/test.txt", out _));
});

Check("prompt composer orders modules and resolves project variables", () =>
{
    PromptModuleEntry third = new() { Title = "Third", SortOrder = 30, Body = "Server={{server}} Chat={{chatgpt}}" };
    PromptModuleEntry second = new() { Title = "Second", SortOrder = 20, Body = "Repo={{repo}}" };
    PromptModuleEntry first = new() { Title = "First", SortOrder = 10, Body = "Project {{project}}" };
    ProjectEntry project = new()
    {
        Name = "Demo",
        RepoUrl = "https://github.com/example/demo",
        ServerUrl = "https://vercel.com/example/demo",
        ChatGptUrl = "https://chatgpt.com/c/demo",
    };
    string text = PromptComposer.Compose([third, second, first], project);
    Equal(
        "Project Demo"
        + Environment.NewLine + Environment.NewLine
        + "Repo=https://github.com/example/demo"
        + Environment.NewLine + Environment.NewLine
        + "Server=https://vercel.com/example/demo Chat=https://chatgpt.com/c/demo",
        text);
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

Check("Power Ops portal, snippet and transcript data round-trip", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityPowerOps-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        ProjectEntry linkedProject = state.Projects.First();
        state.RepositoryLists.Add(new RepositoryListEntry
        {
            Name = "Foil Core",
            Items = [new RepositoryListItemEntry { ProjectId = linkedProject.Id, IncludeRepo = true, IncludeSite = false }],
        });
        state.Portals.Add(new PortalEntry
        {
            Name = "Vercel",
            Category = "Deploy",
            MainUrl = "https://vercel.com/",
            IsPinnedToRibbon = true,
            Links = [new PortalLinkEntry { Label = "Project", Url = "https://vercel.com/example/project" }],
        });
        state.ClipboardSnippets.Add(new ClipboardSnippetEntry { Title = "Debug prompt", Text = "Reproduce then fix." });
        state.Notes.Add(new StickyNoteEntry
        {
            Kind = CaptureKind.Transcript,
            Title = "Meeting transcript",
            Text = "Long-form text",
            Url = "https://example.com/source",
        });
        DateTimeOffset due = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        state.Notes.Add(new StickyNoteEntry
        {
            Kind = CaptureKind.Todo,
            Title = "Ship Power Ops",
            Priority = "High",
            DueUtc = due,
        });
        store.Save(state);

        WorkspaceState loaded = store.Load();
        True(loaded.RepositoryLists.Any(item => item.Name == "Foil Core" && item.Items.Count == 1));
        True(loaded.Portals.Any(item => item.Name == "Vercel" && item.Links.Count == 1));
        True(loaded.ClipboardSnippets.Any(item => item.Title == "Debug prompt"));
        True(loaded.Notes.Any(item => item.Kind == CaptureKind.Transcript && item.Url.Contains("example.com")));
        True(loaded.Notes.Any(item => item.Kind == CaptureKind.Todo && item.Priority == "High" && item.DueUtc == due));
        True(loaded.SchemaVersion == WorkspaceState.CurrentSchemaVersion);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("backup recovery does not overwrite the good backup with corrupt primary", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityRecovery-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "KnownGood" });
        store.Save(state);
        string backupBefore = File.ReadAllText(store.BackupFilePath);
        File.WriteAllText(store.DataFilePath, "{corrupt");

        WorkspaceState recovered = store.Load();
        True(recovered.Projects.Count > 0);
        Equal(backupBefore, File.ReadAllText(store.BackupFilePath));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("empty object import is rejected without replacing the live workspace", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityImportGuard-" + Guid.NewGuid().ToString("N"));
    string importPath = Path.Combine(root, "empty.json");
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "KeepMe", RepoUrl = "https://github.com/example/keep" });
        store.Save(state);
        string primaryBefore = File.ReadAllText(store.DataFilePath);
        File.WriteAllText(importPath, "{}");

        bool threw = false;
        try
        {
            store.Import(importPath);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        True(threw);
        Equal(primaryBefore, File.ReadAllText(store.DataFilePath));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("future workspace schema is refused without rewriting the source file", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityFutureSchema-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "workspace.json");
        string future = "{\"SchemaVersion\":999,\"Projects\":[{\"Name\":\"Future\"}],\"Notes\":[]}";
        File.WriteAllText(path, future);

        WorkspaceStore store = new(root);
        bool threw = false;
        try
        {
            store.Load();
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        True(threw);
        Equal(future, File.ReadAllText(path));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("null collection entries are ignored during normalization", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityNullEntries-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "workspace.json"),
            "{\"SchemaVersion\":3,\"Projects\":[null,{\"Name\":\"Valid\"}],\"Portals\":[null],\"ClipboardSnippets\":[null],\"PromptModules\":[],\"RecentPrompts\":[],\"Notes\":[null]}");

        WorkspaceStore store = new(root);
        WorkspaceState loaded = store.Load();
        True(loaded.Projects.Count == 1);
        Equal("Valid", loaded.Projects[0].Name);
        True(loaded.Portals.Count == 0);
        True(loaded.Notes.Count == 0);
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
