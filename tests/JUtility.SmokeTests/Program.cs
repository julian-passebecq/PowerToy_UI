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

Check("resource URL classifier recognizes common providers and kinds", () =>
{
    True(ResourceCatalogService.TryClassify("https://github.com/example/demo", out ResourceUrlClassification github));
    Equal("GitHub", github.Provider);
    Equal("Repository", github.Kind);
    Equal("demo", github.SuggestedName);

    True(ResourceCatalogService.TryClassify("https://drive.google.com/drive/folders/abc123", out ResourceUrlClassification drive));
    Equal("Google Drive", drive.Provider);
    Equal("Folder", drive.Kind);

    True(ResourceCatalogService.TryClassify("https://docs.google.com/document/d/abc123/edit", out ResourceUrlClassification docs));
    Equal("Google Drive", docs.Provider);
    Equal("Document", docs.Kind);

    True(ResourceCatalogService.TryClassify("https://www.dropbox.com/scl/fo/abc/example", out ResourceUrlClassification dropbox));
    Equal("Dropbox", dropbox.Provider);
    Equal("Folder", dropbox.Kind);

    True(ResourceCatalogService.TryClassify("https://tenant.sharepoint.com/sites/team/Shared%20Documents", out ResourceUrlClassification sharepoint));
    Equal("SharePoint", sharepoint.Provider);
    Equal("Folder", sharepoint.Kind);

    True(ResourceCatalogService.TryClassify("https://www.notion.so/workspace/Page-123456", out ResourceUrlClassification notion));
    Equal("Notion", notion.Provider);
    Equal("Page", notion.Kind);
});

Check("Resource Hub provider and group filters combine instead of overriding each other", () =>
{
    WorkspaceResourceEntry githubAtlas = new()
    {
        Name = "Atlas repo",
        Provider = "GitHub",
        Kind = "Repository",
        Group = "Atlas",
        Url = "https://github.com/example/atlas",
        IsFavorite = true,
    };
    WorkspaceResourceEntry driveAtlas = new()
    {
        Name = "Atlas docs",
        Provider = "Google Drive",
        Kind = "Folder",
        Group = "Atlas",
        Url = "https://drive.google.com/drive/folders/atlas",
        IsPinned = true,
    };
    HashSet<string> githubOnly = new(StringComparer.OrdinalIgnoreCase) { "GitHub" };

    True(ResourceCatalogService.MatchesFilter(githubAtlas, githubOnly, "group:Atlas", ""));
    False(ResourceCatalogService.MatchesFilter(driveAtlas, githubOnly, "group:Atlas", ""));
    True(ResourceCatalogService.MatchesFilter(githubAtlas, githubOnly, "favorites", ""));
    False(ResourceCatalogService.MatchesFilter(driveAtlas, githubOnly, "pinned", ""));
    True(ResourceCatalogService.MatchesFilter(githubAtlas, null, "all", "atlas repo"));
    False(ResourceCatalogService.MatchesFilter(githubAtlas, null, "all", "dropbox"));
});

Check("resource URL classifier recognizes OneDrive short links", () =>
{
    True(ResourceCatalogService.TryClassify("https://1drv.ms/f/s!example", out ResourceUrlClassification oneDrive));
    Equal("OneDrive", oneDrive.Provider);
    Equal("Folder", oneDrive.Kind);
});

Check("resource URL upsert deduplicates equivalent links", () =>
{
    List<WorkspaceResourceEntry> resources = [];

    ResourceUpsertResult first = ResourceCatalogService.UpsertUrl(resources, "github.com/example/demo/");
    ResourceUpsertResult second = ResourceCatalogService.UpsertUrl(resources, "https://GITHUB.com/example/demo");

    True(first.Added);
    False(second.Added);
    True(resources.Count == 1);
    True(ReferenceEquals(first.Resource, second.Resource));
    Equal("GitHub", resources[0].Provider);
    Equal("Repository", resources[0].Kind);
});

Check("resource URL equivalence normalizes host but preserves path case", () =>
{
    True(ResourceCatalogService.UrlsEquivalent(
        "https://GITHUB.com/example/demo/",
        "https://github.com/example/demo"));

    False(ResourceCatalogService.UrlsEquivalent(
        "https://drive.google.com/drive/folders/AbC123",
        "https://drive.google.com/drive/folders/abc123"));
});

Check("repository resource import is idempotent and ignores archived projects", () =>
{
    List<WorkspaceResourceEntry> resources = [];
    ProjectEntry active = new()
    {
        Name = "Active",
        Category = "Atlas",
        RepoUrl = "github.com/example/active",
    };
    ProjectEntry archived = new()
    {
        Name = "Archived",
        RepoUrl = "https://github.com/example/archived",
        IsArchived = true,
    };

    ResourceImportSummary first = ResourceCatalogService.ImportProjects(resources, [active, archived]);
    ResourceImportSummary second = ResourceCatalogService.ImportProjects(resources, [active, archived]);

    True(first.Added == 1 && first.Updated == 0 && first.Total == 1);
    True(second.Added == 0 && second.Updated == 1 && second.Total == 1);
    True(resources.Count == 1);
    Equal("GitHub", resources[0].Provider);
    Equal("Repository", resources[0].Kind);
    Equal("Atlas", resources[0].Group);
    True(resources[0].SourceProjectId == active.Id);
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

Check("prompt composer discovers and replaces arbitrary variables", () =>
{
    PromptModuleEntry module = new()
    {
        Title = "Custom",
        SortOrder = 10,
        Body = "Deploy {{environment}} in {{region}} for {{project}}",
    };
    ProjectEntry project = new() { Name = "Atlas" };

    IReadOnlyList<string> variables = PromptComposer.FindVariables([module]);
    True(variables.Contains("environment"));
    True(variables.Contains("region"));

    string text = PromptComposer.Compose(
        [module],
        project,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["environment"] = "staging",
            ["region"] = "eu-west",
        });

    Equal("Deploy staging in eu-west for Atlas", text);
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
        state.Resources.Add(new WorkspaceResourceEntry
        {
            Name = "Architecture",
            Provider = "Google Drive",
            Kind = "Folder",
            Group = "Datapass",
            Url = "https://drive.google.com/drive/folders/example",
            IsPinned = true,
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
        True(loaded.Resources.Any(item => item.Name == "Architecture" && item.Provider == "Google Drive" && item.IsPinned));
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
        string[] evidence = Directory.GetFiles(root, "workspace.invalid.*.json");
        True(evidence.Length == 1);
        Equal("{corrupt", File.ReadAllText(evidence[0]));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("malformed primary without backup is preserved and never silently reseeded", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityMalformedNoBackup-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        string primary = Path.Combine(root, "workspace.json");
        File.WriteAllText(primary, "{broken");

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
        Equal("{broken", File.ReadAllText(primary));
        False(File.Exists(store.BackupFilePath));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("invalid backup without primary is preserved and never silently reseeded", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityInvalidBackupOnly-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        string backup = Path.Combine(root, "workspace.backup.json");
        File.WriteAllText(backup, "{broken-backup");

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
        Equal("{broken-backup", File.ReadAllText(backup));
        False(File.Exists(store.DataFilePath));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("workspace save normalizes a detached snapshot without mutating caller objects", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityDetachedSave-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        ProjectEntry project = new()
        {
            Name = "  Spaced project  ",
            Category = "  ",
            Subcategory = "  ",
        };
        state.Projects.Add(project);
        state.SchemaVersion = 3;

        store.Save(state);

        Equal("  Spaced project  ", project.Name);
        Equal("  ", project.Category);
        True(state.SchemaVersion == 3);

        WorkspaceState loaded = store.Load();
        ProjectEntry persisted = loaded.Projects.Single(item => item.Id == project.Id);
        Equal("Spaced project", persisted.Name);
        Equal("Projects", persisted.Category);
        Equal("Misc", persisted.Subcategory);
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

Check("save never rotates malformed primary bytes over last-known-good backup", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityBackupRotationGuard-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "GoodGeneration" });
        store.Save(state);
        state.Projects.Add(new ProjectEntry { Name = "NewestGeneration" });
        store.Save(state);

        string backupBefore = File.ReadAllText(store.BackupFilePath);
        True(backupBefore.Contains("GoodGeneration", StringComparison.Ordinal));
        False(backupBefore.Contains("NewestGeneration", StringComparison.Ordinal));

        File.WriteAllText(store.DataFilePath, "{externally-corrupt");
        state.Projects.Add(new ProjectEntry { Name = "RecoveredFromMemory" });
        store.Save(state);

        Equal(backupBefore, File.ReadAllText(store.BackupFilePath));
        string[] evidence = Directory.GetFiles(root, "workspace.invalid.*.json");
        True(evidence.Any(path => File.ReadAllText(path) == "{externally-corrupt"));

        WorkspaceState loaded = store.Load();
        True(loaded.Projects.Any(project => project.Name == "RecoveredFromMemory"));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("locked primary does not fall back to backup or rewrite files", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityLockedPrimary-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "LockedPrimaryMarker" });
        store.Save(state);

        string primaryBefore = File.ReadAllText(store.DataFilePath);
        string backupBefore = File.ReadAllText(store.BackupFilePath);

        bool threw = false;
        using (FileStream held = new(store.DataFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try
            {
                store.Load();
            }
            catch (IOException)
            {
                threw = true;
            }
        }

        True(threw);
        Equal(primaryBefore, File.ReadAllText(store.DataFilePath));
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

Check("empty primary workspace recovers from the last good backup", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityEmptyPrimary-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "BackupMarker", RepoUrl = "https://github.com/example/backup" });
        store.Save(state);
        state.Projects.Add(new ProjectEntry { Name = "SecondSave" });
        store.Save(state);

        File.WriteAllText(store.DataFilePath, "{}");
        WorkspaceState recovered = store.Load();

        True(recovered.Projects.Any(project => project.Name == "BackupMarker"));
        False(recovered.Projects.Any(project => project.Name == "SecondSave"));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("workspace export is atomic and rejects managed workspace targets", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityExportSafety-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        ProjectEntry project = new() { Name = "  Exported  ", Category = "  " };
        state.Projects.Add(project);

        string exportPath = Path.Combine(root, "portable-export.json");
        File.WriteAllText(exportPath, "old export bytes");
        store.Export(state, exportPath);

        string exportedText = File.ReadAllText(exportPath);
        True(exportedText.Contains("Exported", StringComparison.Ordinal));
        False(exportedText.Contains("old export bytes", StringComparison.Ordinal));
        Equal("  Exported  ", project.Name);
        Equal("  ", project.Category);
        True(Directory.GetFiles(root, ".portable-export.json.*.tmp").Length == 0);

        string primaryBefore = File.ReadAllText(store.DataFilePath);
        bool primaryRejected = false;
        try
        {
            store.Export(state, store.DataFilePath);
        }
        catch (InvalidOperationException)
        {
            primaryRejected = true;
        }
        True(primaryRejected);
        Equal(primaryBefore, File.ReadAllText(store.DataFilePath));

        // A backup exists after one additional save generation.
        store.Save(state);
        string backupBefore = File.ReadAllText(store.BackupFilePath);
        bool backupRejected = false;
        try
        {
            store.Export(state, store.BackupFilePath);
        }
        catch (InvalidOperationException)
        {
            backupRejected = true;
        }
        True(backupRejected);
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
            "{\"SchemaVersion\":4,\"Projects\":[null,{\"Name\":\"Valid\"}],\"Portals\":[null],\"Resources\":[null],\"ClipboardSnippets\":[null],\"PromptModules\":[],\"RecentPrompts\":[],\"Notes\":[null]}");

        WorkspaceStore store = new(root);
        WorkspaceState loaded = store.Load();
        True(loaded.Projects.Count == 1);
        Equal("Valid", loaded.Projects[0].Name);
        True(loaded.Portals.Count == 0);
        True(loaded.Resources.Count == 0);
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

Check("nullable optional workspace strings normalize safely without losing free-form text", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityNullStrings-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        Guid projectId = Guid.NewGuid();
        Guid resourceId = Guid.NewGuid();
        Guid noteId = Guid.NewGuid();
        string json = $"""
        {
          "SchemaVersion": 4,
          "Preferences": { "GitHubOwner": null, "LastModule": null },
          "Projects": [
            {
              "Id": "{{projectId}}",
              "Name": " Project ",
              "Category": null,
              "Subcategory": null,
              "Note": null,
              "RepoUrl": null,
              "SiteUrl": "  https://example.com/site  "
            }
          ],
          "RepositoryLists": [],
          "Portals": [],
          "Resources": [
            {
              "Id": "{{resourceId}}",
              "Name": " Drive ",
              "Provider": null,
              "Kind": null,
              "Group": null,
              "Url": null,
              "Note": null
            }
          ],
          "ClipboardSnippets": [],
          "PromptModules": [],
          "RecentPrompts": [],
          "Notes": [
            {
              "Id": "{{noteId}}",
              "Kind": 2,
              "Title": " Note ",
              "Subject": null,
              "Text": null,
              "Url": null,
              "Labels": null,
              "Status": null,
              "Priority": null
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(root, "workspace.json"), json);

        WorkspaceStore store = new(root);
        WorkspaceState loaded = store.Load();

        Equal("julian-passebecq", loaded.Preferences.GitHubOwner);
        Equal("Dashboard", loaded.Preferences.LastModule);
        Equal("Project", loaded.Projects[0].Name);
        Equal("Projects", loaded.Projects[0].Category);
        Equal("Misc", loaded.Projects[0].Subcategory);
        Equal("", loaded.Projects[0].Note);
        Equal("", loaded.Projects[0].RepoUrl);
        Equal("https://example.com/site", loaded.Projects[0].SiteUrl);
        Equal("Other", loaded.Resources[0].Provider);
        Equal("Link", loaded.Resources[0].Kind);
        Equal("General", loaded.Resources[0].Group);
        Equal("", loaded.Resources[0].Url);
        Equal("", loaded.Resources[0].Note);
        Equal("", loaded.Notes[0].Subject);
        Equal("", loaded.Notes[0].Text);
        Equal("", loaded.Notes[0].Url);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("duplicate IDs and unsupported enum values are rejected without rewriting source", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityValidationGuard-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "workspace.json");
        Guid duplicate = Guid.NewGuid();
        string invalid = $"""
        {
          "SchemaVersion": 4,
          "Preferences": { "WindowBehavior": 999 },
          "Projects": [
            { "Id": "{{duplicate}}", "Name": "A" },
            { "Id": "{{duplicate}}", "Name": "B" }
          ],
          "RepositoryLists": [],
          "Portals": [],
          "Resources": [],
          "ClipboardSnippets": [],
          "PromptModules": [],
          "RecentPrompts": [],
          "Notes": []
        }
        """;
        File.WriteAllText(path, invalid);

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
        Equal(invalid, File.ReadAllText(path));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("duplicate workspace item IDs are rejected without rewriting source", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityDuplicateIds-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "workspace.json");
        Guid duplicate = Guid.NewGuid();
        string invalid = $"""
        {
          "SchemaVersion": 4,
          "Projects": [
            { "Id": "{{duplicate}}", "Name": "A" },
            { "Id": "{{duplicate}}", "Name": "B" }
          ],
          "RepositoryLists": [],
          "Portals": [],
          "Resources": [],
          "ClipboardSnippets": [],
          "PromptModules": [],
          "RecentPrompts": [],
          "Notes": []
        }
        """;
        File.WriteAllText(path, invalid);

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
        Equal(invalid, File.ReadAllText(path));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("recent prompt history is deduplicated and capped during normalization", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityRecentHistory-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.RecentPrompts.Clear();

        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < 35; index++)
        {
            state.RecentPrompts.Add(new RecentPromptEntry
            {
                Title = $"Prompt {index}",
                Text = $"text-{index}",
                CreatedUtc = start.AddMinutes(index),
            });
        }

        state.RecentPrompts.Add(new RecentPromptEntry
        {
            Title = "Newest duplicate",
            Text = "text-34",
            CreatedUtc = start.AddHours(10),
        });
        state.RecentPrompts.Add(new RecentPromptEntry
        {
            Title = "Empty",
            Text = "   ",
            CreatedUtc = start.AddHours(11),
        });

        store.Save(state);
        WorkspaceState loaded = store.Load();

        True(loaded.RecentPrompts.Count == 30);
        True(loaded.RecentPrompts.Count(prompt => prompt.Text == "text-34") == 1);
        Equal("Newest duplicate", loaded.RecentPrompts.First(prompt => prompt.Text == "text-34").Title);
        False(loaded.RecentPrompts.Any(prompt => string.IsNullOrWhiteSpace(prompt.Text)));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("v3 workspace migrates to schema v4 with an empty Resource Hub", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityV3ResourceMigration-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "workspace.json"),
            "{\"SchemaVersion\":3,\"Projects\":[{\"Name\":\"LegacyProject\"}],\"Portals\":[],\"ClipboardSnippets\":[],\"PromptModules\":[],\"RecentPrompts\":[],\"Notes\":[]}");

        WorkspaceStore store = new(root);
        WorkspaceState loaded = store.Load();

        True(loaded.SchemaVersion == WorkspaceState.CurrentSchemaVersion);
        True(loaded.Resources.Count == 0);
        True(loaded.Projects.Any(project => project.Name == "LegacyProject"));
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

Check("last module preference round-trips", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityLastModule-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Preferences.LastModule = "Portals";
        store.Save(state);

        WorkspaceState loaded = store.Load();
        Equal("Portals", loaded.Preferences.LastModule);
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
