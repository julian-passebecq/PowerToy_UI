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

Check("Sidebar quick access only includes curated portals resources and snippets", () =>
{
    PortalEntry plainPortal = new();
    PortalEntry pinnedPortal = new() { IsPinnedToRibbon = true };
    PortalEntry favoritePortal = new() { IsFavorite = true };
    False(SidebarQuickAccessPolicy.IncludePortal(plainPortal));
    True(SidebarQuickAccessPolicy.IncludePortal(pinnedPortal));
    True(SidebarQuickAccessPolicy.IncludePortal(favoritePortal));

    WorkspaceResourceEntry plainResource = new();
    WorkspaceResourceEntry pinnedResource = new() { IsPinned = true };
    WorkspaceResourceEntry favoriteResource = new() { IsFavorite = true };
    False(SidebarQuickAccessPolicy.IncludeResource(plainResource));
    True(SidebarQuickAccessPolicy.IncludeResource(pinnedResource));
    True(SidebarQuickAccessPolicy.IncludeResource(favoriteResource));

    False(SidebarQuickAccessPolicy.IncludeSnippet(new ClipboardSnippetEntry()));
    True(SidebarQuickAccessPolicy.IncludeSnippet(new ClipboardSnippetEntry { IsPinned = true }));
});

Check("Sidebar capture policy excludes archived and optionally completed captures", () =>
{
    StickyNoteEntry active = new();
    StickyNoteEntry completed = new() { IsCompleted = true };
    StickyNoteEntry archived = new() { IsArchived = true };

    True(SidebarQuickAccessPolicy.IncludeCapture(active, includeCompleted: false));
    False(SidebarQuickAccessPolicy.IncludeCapture(completed, includeCompleted: false));
    True(SidebarQuickAccessPolicy.IncludeCapture(completed, includeCompleted: true));
    False(SidebarQuickAccessPolicy.IncludeCapture(archived, includeCompleted: true));
});

Check("GitHub repository merge preserves manually maintained cockpit links and metadata", () =>
{
    DateTimeOffset updated = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
    ProjectEntry existing = new()
    {
        Name = "Atlas Note custom",
        Category = "My Atlas",
        Subcategory = "Important",
        Note = "Keep my note",
        GitHubFullName = "julian-passebecq/atlasnote",
        RepoUrl = "https://github.com/julian-passebecq/atlasnote",
        SiteUrl = "https://my-custom-site.example/",
        ServerUrl = "https://server.example/",
        ChatGptUrl = "https://chatgpt.com/c/custom",
    };
    List<ProjectEntry> projects = [existing];

    RepositoryMergeSummary summary = RepositoryCatalogService.MergeGitHubRepositories(
        projects,
        [
            new GitHubRepositorySnapshot(
                "atlasnote",
                "julian-passebecq/atlasnote",
                "https://github.com/julian-passebecq/atlasnote/",
                "GitHub description",
                "https://github-homepage.example/",
                "TypeScript",
                true,
                updated),
        ]);

    True(summary.Added == 0 && summary.Updated == 1 && summary.Total == 1);
    Equal("https://github.com/julian-passebecq/atlasnote/", existing.RepoUrl);
    Equal("https://my-custom-site.example/", existing.SiteUrl);
    Equal("https://server.example/", existing.ServerUrl);
    Equal("https://chatgpt.com/c/custom", existing.ChatGptUrl);
    Equal("My Atlas", existing.Category);
    Equal("Important", existing.Subcategory);
    Equal("Keep my note", existing.Note);
    Equal("TypeScript", existing.Language);
    True(existing.IsGitHubPrivate);
    True(existing.GitHubUpdatedUtc == updated);
});

Check("GitHub repository merge fills missing homepage and classifies new projects without duplicates", () =>
{
    List<ProjectEntry> projects = [];
    GitHubRepositorySnapshot snapshot = new(
        "atlascode",
        "julian-passebecq/atlascode",
        "https://github.com/julian-passebecq/atlascode",
        "Code workspace",
        "https://atlascode.example/",
        "C#",
        false,
        null);

    RepositoryMergeSummary first = RepositoryCatalogService.MergeGitHubRepositories(projects, [snapshot]);
    RepositoryMergeSummary second = RepositoryCatalogService.MergeGitHubRepositories(projects, [snapshot]);

    True(first.Added == 1 && first.Updated == 0 && first.Total == 1);
    True(second.Added == 0 && second.Updated == 1 && second.Total == 1);
    True(projects.Count == 1);
    Equal("Atlas", projects[0].Category);
    Equal("Core", projects[0].Subcategory);
    Equal("https://atlascode.example/", projects[0].SiteUrl);
    False(projects[0].IncludeInCopyAll);
});

Check("GitHub repository merge only fills an empty existing homepage", () =>
{
    ProjectEntry existing = new()
    {
        Name = "Datapass",
        RepoUrl = "https://github.com/example/datapass",
        SiteUrl = "",
        Category = "Projects",
        Subcategory = "Misc",
    };
    List<ProjectEntry> projects = [existing];

    RepositoryCatalogService.MergeGitHubRepositories(
        projects,
        [
            new GitHubRepositorySnapshot(
                "datapass",
                "example/datapass",
                "https://github.com/example/datapass",
                "Data engineering workbench",
                "https://datapass.example/",
                "C#",
                false,
                null),
        ]);

    Equal("https://datapass.example/", existing.SiteUrl);
    Equal("Datapass", existing.Category);
    Equal("Core", existing.Subcategory);
    Equal("Data engineering workbench", existing.Note);
});

Check("starter catalog adds common cockpit portals and snippets idempotently", () =>
{
    List<PortalEntry> portals = [];
    List<ClipboardSnippetEntry> snippets = [];

    StarterCatalogSummary first = StarterCatalogService.Merge(portals, snippets);
    True(first.PortalsAdded >= 10);
    True(first.SnippetsAdded >= 3);
    True(portals.Any(portal => portal.Name == "GitHub" && portal.IsPinnedToRibbon));
    True(portals.Any(portal => portal.Name == "Microsoft Fabric" && portal.MainUrl.Contains("app.fabric.microsoft.com", StringComparison.OrdinalIgnoreCase)));
    True(portals.Any(portal => portal.Name == "Databricks"));
    True(portals.Any(portal => portal.Name == "Vercel"));
    True(portals.Any(portal => portal.Name == "Netlify"));
    True(portals.Any(portal => portal.Name == "Cloudflare"));
    True(portals.Any(portal => portal.Name == "Google Drive"));
    True(portals.Any(portal => portal.Name == "Dropbox"));
    True(portals.Any(portal => portal.Name == "LinkedIn"));
    True(portals.Any(portal => portal.Name == "ChatGPT"));
    True(portals.Any(portal => portal.Name == "MongoDB Atlas"));
    True(portals.Any(portal => portal.Name == "Codex"));
    True(portals.Any(portal => portal.Name == "Claude"));
    PortalEntry github = portals.Single(portal => portal.Name == "GitHub");
    True(github.QuickActions.Count == 3);
    True(github.QuickActions.Any(action => action.Label == "New repo"));
    True(snippets.Any(snippet => snippet.Title == "Continue project" && snippet.IsPinned));
    True(snippets.Any(snippet => snippet.Title == "Debug and verify" && snippet.IsPinned));

    int portalCount = portals.Count;
    int snippetCount = snippets.Count;
    StarterCatalogSummary second = StarterCatalogService.Merge(portals, snippets);

    True(second.TotalAdded == 0);
    True(portals.Count == portalCount);
    True(snippets.Count == snippetCount);
});

Check("starter catalog respects an existing service portal even when its main path differs", () =>
{
    PortalEntry existingVercel = new()
    {
        Name = "Vercel",
        Category = "My deploy",
        MainUrl = "https://vercel.com/",
        IsPinnedToRibbon = false,
    };
    List<PortalEntry> portals = [existingVercel];
    List<ClipboardSnippetEntry> snippets = [];

    StarterCatalogService.Merge(portals, snippets);

    True(portals.Count(portal => portal.Name.Equals("Vercel", StringComparison.OrdinalIgnoreCase)) == 1);
    Equal("https://vercel.com/", existingVercel.MainUrl);
    Equal("My deploy", existingVercel.Category);
    False(existingVercel.IsPinnedToRibbon);
});

Check("fresh workspace seed includes the starter cockpit", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityStarterSeed-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState seeded = store.Load();

        True(seeded.Portals.Any(portal => portal.Name == "GitHub"));
        True(seeded.Portals.Any(portal => portal.Name == "Microsoft Fabric"));
        True(seeded.Portals.Any(portal => portal.Name == "Vercel"));
        True(seeded.Tools.Any(tool => tool.Name == "VS Code" && tool.Command == "code"));
        True(seeded.Tools.Any(tool => tool.Name == "Windows Terminal" && tool.Command == "wt"));
        True(seeded.ClipboardSnippets.Any(snippet => snippet.Title == "Continue project"));
        True(seeded.ClipboardSnippets.Any(snippet => snippet.Title == "Debug and verify"));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("starter catalog preserves customized entries instead of overwriting them", () =>
{
    PortalEntry customPortal = new()
    {
        Name = "My GitHub",
        Category = "Personal",
        MainUrl = "https://github.com",
        IsPinnedToRibbon = false,
    };
    ClipboardSnippetEntry customSnippet = new()
    {
        Title = "Debug and verify",
        Category = "Mine",
        Text = "My custom workflow",
        IsPinned = false,
    };
    List<PortalEntry> portals = [customPortal];
    List<ClipboardSnippetEntry> snippets = [customSnippet];

    StarterCatalogService.Merge(portals, snippets);

    True(portals.Count(portal => ResourceCatalogService.UrlsEquivalent(portal.MainUrl, "https://github.com/")) == 1);
    Equal("My GitHub", customPortal.Name);
    Equal("Personal", customPortal.Category);
    False(customPortal.IsPinnedToRibbon);
    True(snippets.Count(snippet => snippet.Title.Equals("Debug and verify", StringComparison.OrdinalIgnoreCase)) == 1);
    Equal("My custom workflow", customSnippet.Text);
    Equal("Mine", customSnippet.Category);
    False(customSnippet.IsPinned);
});

Check("resource URL classifier recognizes common providers and kinds", () =>
{
    True(ResourceCatalogService.TryClassify("https://github.com/example/demo", out ResourceUrlClassification github));
    Equal("GitHub", github.Provider);
    Equal("Repository", github.Kind);
    Equal("demo", github.SuggestedName);

    True(ResourceCatalogService.TryClassify("https://github.com/example/demo/issues/12", out ResourceUrlClassification githubIssue));
    Equal("GitHub", githubIssue.Provider);
    Equal("Link", githubIssue.Kind);

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

Check("Resource Hub direct provider filters match only that provider", () =>
{
    WorkspaceResourceEntry github = new()
    {
        Name = "Repo",
        Provider = "GitHub",
        Kind = "Repository",
        Url = "https://github.com/example/repo",
    };
    WorkspaceResourceEntry drive = new()
    {
        Name = "Docs",
        Provider = "Google Drive",
        Kind = "Folder",
        Url = "https://drive.google.com/drive/folders/docs",
    };

    True(ResourceCatalogService.MatchesFilter(github, null, "GitHub", ""));
    False(ResourceCatalogService.MatchesFilter(drive, null, "GitHub", ""));
    True(ResourceCatalogService.MatchesFilter(drive, null, "Google Drive", ""));
    False(ResourceCatalogService.MatchesFilter(github, null, "Google Drive", ""));
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

    True(ResourceCatalogService.UrlsEquivalent(
        "https://github.com/Example/Demo",
        "https://github.com/example/demo/"));

    False(ResourceCatalogService.UrlsEquivalent(
        "https://drive.google.com/drive/folders/AbC123",
        "https://drive.google.com/drive/folders/abc123"));
});

Check("resource URL upsert preserves user metadata on duplicates", () =>
{
    WorkspaceResourceEntry existing = new()
    {
        Name = "My important docs",
        Provider = "Google Drive",
        Kind = "Folder",
        Group = "Atlas",
        Url = "https://drive.google.com/drive/folders/ABC123",
        Note = "Keep this note",
        IsPinned = true,
        IsFavorite = true,
    };
    List<WorkspaceResourceEntry> resources = [existing];

    ResourceUpsertResult result = ResourceCatalogService.UpsertUrl(
        resources,
        "https://drive.google.com/drive/folders/ABC123/",
        "Other");

    False(result.Added);
    True(resources.Count == 1);
    Equal("My important docs", existing.Name);
    Equal("Atlas", existing.Group);
    Equal("Keep this note", existing.Note);
    True(existing.IsPinned);
    True(existing.IsFavorite);
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

Check("repository resource refresh preserves user labels groups notes and pins", () =>
{
    ProjectEntry project = new()
    {
        Name = "Original project name",
        Category = "Atlas",
        RepoUrl = "https://github.com/example/original",
    };
    List<WorkspaceResourceEntry> resources = [];

    ResourceCatalogService.ImportProjects(resources, [project]);
    WorkspaceResourceEntry resource = resources.Single();
    resource.Name = "My shortcut";
    resource.Group = "Daily work";
    resource.Note = "Do not lose this";
    resource.IsPinned = true;
    resource.IsFavorite = true;

    project.Name = "Renamed repository";
    project.Category = "Portfolio";
    project.RepoUrl = "https://github.com/example/renamed";

    ResourceImportSummary refreshed = ResourceCatalogService.ImportProjects(resources, [project]);

    True(refreshed.Added == 0 && refreshed.Updated == 1);
    Equal("My shortcut", resource.Name);
    Equal("Daily work", resource.Group);
    Equal("Do not lose this", resource.Note);
    True(resource.IsPinned);
    True(resource.IsFavorite);
    Equal("https://github.com/example/renamed", resource.Url);
    True(resource.SourceProjectId == project.Id);
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
            QuickActions = [new PortalLinkEntry { Label = "New project", Url = "https://vercel.com/new" }],
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
        True(loaded.Portals.Any(item => item.Name == "Vercel" && item.QuickActions.Count == 1 && item.Links.Count == 1));
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

Check("save refuses to overwrite an externally replaced future-schema primary", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityFuturePrimarySaveGuard-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "BeforeFuture" });
        store.Save(state);
        string backupBefore = File.ReadAllText(store.BackupFilePath);

        string future = "{\"SchemaVersion\":999,\"Projects\":[{\"Name\":\"ExternalFuture\"}],\"Notes\":[]}";
        File.WriteAllText(store.DataFilePath, future);
        state.Projects.Add(new ProjectEntry { Name = "MustNotPublish" });

        bool threw = false;
        try
        {
            store.Save(state);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        True(threw);
        Equal(future, File.ReadAllText(store.DataFilePath));
        Equal(backupBefore, File.ReadAllText(store.BackupFilePath));
        True(Directory.GetFiles(root, "workspace.save.*.tmp").Length == 0);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("backup-copy failure leaves primary unchanged and cleans save temp", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityBackupCopyFailure-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "StablePrimary" });
        store.Save(state);

        string primaryBefore = File.ReadAllText(store.DataFilePath);
        if (File.Exists(store.BackupFilePath))
        {
            File.Delete(store.BackupFilePath);
        }
        Directory.CreateDirectory(store.BackupFilePath);

        state.Projects.Add(new ProjectEntry { Name = "MustNotPublish" });
        bool threw = false;
        try
        {
            store.Save(state);
        }
        catch (IOException)
        {
            threw = true;
        }
        catch (UnauthorizedAccessException)
        {
            threw = true;
        }

        True(threw);
        Equal(primaryBefore, File.ReadAllText(store.DataFilePath));
        True(Directory.Exists(store.BackupFilePath));
        True(Directory.GetFiles(root, "workspace.save.*.tmp").Length == 0);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("save with locked primary publishes nothing and cleans save temp", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityLockedSave-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Add(new ProjectEntry { Name = "StableBeforeLock" });
        store.Save(state);

        string primaryBefore = File.ReadAllText(store.DataFilePath);
        string backupBefore = File.ReadAllText(store.BackupFilePath);
        state.Projects.Add(new ProjectEntry { Name = "MustNotPublish" });

        bool threw = false;
        using (FileStream held = new(store.DataFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try
            {
                store.Save(state);
            }
            catch (IOException)
            {
                threw = true;
            }
        }

        True(threw);
        Equal(primaryBefore, File.ReadAllText(store.DataFilePath));
        Equal(backupBefore, File.ReadAllText(store.BackupFilePath));
        True(Directory.GetFiles(root, "workspace.save.*.tmp").Length == 0);
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

Check("import preparation is read-only until explicit commit", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityPrepareImport-" + Guid.NewGuid().ToString("N"));
    string sourceDir = Path.Combine(Path.GetTempPath(), "JUtilityPrepareImportSource-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState current = store.Load();
        current.Projects.Add(new ProjectEntry { Name = "CurrentWorkspace" });
        store.Save(current);

        Directory.CreateDirectory(sourceDir);
        WorkspaceStore sourceStore = new(sourceDir);
        WorkspaceState incoming = sourceStore.Load();
        incoming.Projects.Clear();
        incoming.Projects.Add(new ProjectEntry { Name = "ImportedWorkspace" });
        string importPath = Path.Combine(sourceDir, "candidate.json");
        sourceStore.Export(incoming, importPath);

        string primaryBefore = File.ReadAllText(store.DataFilePath);
        string backupBefore = File.ReadAllText(store.BackupFilePath);

        WorkspaceState candidate = store.PrepareImport(importPath);

        True(candidate.Projects.Any(project => project.Name == "ImportedWorkspace"));
        Equal(primaryBefore, File.ReadAllText(store.DataFilePath));
        Equal(backupBefore, File.ReadAllText(store.BackupFilePath));

        WorkspaceState committed = store.CommitImport(candidate);
        True(committed.Projects.Any(project => project.Name == "ImportedWorkspace"));
        True(store.Load().Projects.Any(project => project.Name == "ImportedWorkspace"));
        True(File.ReadAllText(store.BackupFilePath).Contains("CurrentWorkspace", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        if (Directory.Exists(sourceDir))
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }
});

Check("backup-file import can be prepared before current edits rotate the backup", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityBackupImport-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Projects.Clear();
        state.Projects.Add(new ProjectEntry { Name = "GenerationA" });
        store.Save(state);

        state.Projects.Clear();
        state.Projects.Add(new ProjectEntry { Name = "GenerationB" });
        store.Save(state);
        True(File.ReadAllText(store.BackupFilePath).Contains("GenerationA", StringComparison.Ordinal));

        WorkspaceState candidate = store.PrepareImport(store.BackupFilePath);
        True(candidate.Projects.Any(project => project.Name == "GenerationA"));

        // Simulate in-memory edits saved after the user confirms the already-prepared import.
        state.Projects.Clear();
        state.Projects.Add(new ProjectEntry { Name = "UnsavedCurrentEdits" });
        store.Save(state);

        WorkspaceState imported = store.CommitImport(candidate);

        True(imported.Projects.Any(project => project.Name == "GenerationA"));
        True(store.Load().Projects.Any(project => project.Name == "GenerationA"));
        True(File.ReadAllText(store.BackupFilePath).Contains("UnsavedCurrentEdits", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("locked export destination preserves previous export and cleans temp", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityLockedExport-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        string exportPath = Path.Combine(root, "portable.json");
        File.WriteAllText(exportPath, "previous-export");

        bool threw = false;
        using (FileStream held = new(exportPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try
            {
                store.Export(state, exportPath);
            }
            catch (IOException)
            {
                threw = true;
            }
            catch (UnauthorizedAccessException)
            {
                threw = true;
            }
        }

        True(threw);
        Equal("previous-export", File.ReadAllText(exportPath));
        True(Directory.GetFiles(root, ".portable.json.*.tmp").Length == 0);
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
        string json = """
        {
          "SchemaVersion": 4,
          "Preferences": { "GitHubOwner": null, "LastModule": null },
          "Projects": [
            {
              "Id": "__PROJECT_ID__",
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
              "Id": "__RESOURCE_ID__",
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
              "Id": "__NOTE_ID__",
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
        """
            .Replace("__PROJECT_ID__", projectId.ToString(), StringComparison.Ordinal)
            .Replace("__RESOURCE_ID__", resourceId.ToString(), StringComparison.Ordinal)
            .Replace("__NOTE_ID__", noteId.ToString(), StringComparison.Ordinal);
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
        string invalid = """
        {
          "SchemaVersion": 4,
          "Preferences": { "WindowBehavior": 999 },
          "Projects": [
            { "Id": "__DUPLICATE_ID__", "Name": "A" },
            { "Id": "__DUPLICATE_ID__", "Name": "B" }
          ],
          "RepositoryLists": [],
          "Portals": [],
          "Resources": [],
          "ClipboardSnippets": [],
          "PromptModules": [],
          "RecentPrompts": [],
          "Notes": []
        }
        """.Replace("__DUPLICATE_ID__", duplicate.ToString(), StringComparison.Ordinal);
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
        string invalid = """
        {
          "SchemaVersion": 4,
          "Projects": [
            { "Id": "__DUPLICATE_ID__", "Name": "A" },
            { "Id": "__DUPLICATE_ID__", "Name": "B" }
          ],
          "RepositoryLists": [],
          "Portals": [],
          "Resources": [],
          "ClipboardSnippets": [],
          "PromptModules": [],
          "RecentPrompts": [],
          "Notes": []
        }
        """.Replace("__DUPLICATE_ID__", duplicate.ToString(), StringComparison.Ordinal);
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

Check("clipboard media metadata round-trips in schema v6", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityClipboardMedia-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        Guid projectId = state.Projects.First().Id;
        state.ClipboardMedia.Add(new ClipboardMediaEntry
        {
            Kind = ClipboardMediaKind.Image,
            Title = "Architecture screenshot",
            Category = "Datapass",
            Tags = "architecture,reference",
            ProjectId = projectId,
            FileName = "abc.png",
            RelativePath = "media/abc.png",
            MimeType = "image/png",
            ByteLength = 12345,
            IsPinned = true,
        });

        store.Save(state);
        WorkspaceState loaded = store.Load();
        ClipboardMediaEntry media = loaded.ClipboardMedia.Single();

        True(loaded.SchemaVersion == WorkspaceState.CurrentSchemaVersion);
        True(media.Kind == ClipboardMediaKind.Image);
        Equal("Architecture screenshot", media.Title);
        Equal("Datapass", media.Category);
        Equal("media/abc.png", media.RelativePath);
        Equal("", media.ResolvedPath);
        True(media.ProjectId == projectId);
        True(media.ByteLength == 12345);
        True(media.IsPinned);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("schema v5 workspace migrates to v6 with empty clipboard media", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityV5ClipboardMediaMigration-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "workspace.json"),
            "{\"SchemaVersion\":5,\"Projects\":[],\"RepositoryLists\":[],\"Portals\":[],\"Tools\":[],\"Resources\":[],\"ClipboardSnippets\":[],\"PromptModules\":[],\"RecentPrompts\":[],\"Notes\":[]}");

        WorkspaceStore store = new(root);
        WorkspaceState loaded = store.Load();

        True(loaded.SchemaVersion == WorkspaceState.CurrentSchemaVersion);
        True(loaded.ClipboardMedia.Count == 0);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("clipboard media rejects paths outside the managed media directory", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityClipboardMediaPathGuard-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        string[] unsafePaths =
        [
            "workspace.json",
            "../outside.png",
            "media/../workspace.json",
            "media//broken.png",
            OperatingSystem.IsWindows() ? @"C:\temp\unsafe.png" : "/tmp/unsafe.png",
        ];

        foreach (string unsafePath in unsafePaths)
        {
            state.ClipboardMedia.Clear();
            state.ClipboardMedia.Add(new ClipboardMediaEntry
            {
                Title = "Unsafe",
                RelativePath = unsafePath,
            });

            bool threw = false;
            try
            {
                store.Save(state);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }

            True(threw);
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("clipboard media accepts nested managed media paths", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityClipboardMediaNested-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.ClipboardMedia.Add(new ClipboardMediaEntry
        {
            Kind = ClipboardMediaKind.Image,
            Title = "Nested",
            RelativePath = @"media\Datapass\nested.png",
        });

        store.Save(state);
        WorkspaceState loaded = store.Load();

        Equal("media/Datapass/nested.png", loaded.ClipboardMedia.Single().RelativePath);
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

Check("recognizable schema-less workspace is treated as legacy v1", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilitySchemaLessLegacy-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "workspace.json"),
            """
            {
              "Preferences": { "AlwaysOnTop": true },
              "Projects": [],
              "PromptModules": [],
              "RecentPrompts": [],
              "Notes": []
            }
            """);

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

Check("explicit invalid schema zero is rejected without rewriting source", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilitySchemaZero-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "workspace.json");
        string invalid = """
        {
          "SchemaVersion": 0,
          "Projects": [],
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

Check("shell search shortcut requires Ctrl alone and a visible search surface", () =>
{
    True(ShellKeyboardPolicy.ShouldFocusSearch(
        control: true,
        shift: false,
        alt: false,
        windows: false,
        searchAvailable: true));

    False(ShellKeyboardPolicy.ShouldFocusSearch(
        control: true,
        shift: true,
        alt: false,
        windows: false,
        searchAvailable: true));

    False(ShellKeyboardPolicy.ShouldFocusSearch(
        control: true,
        shift: false,
        alt: false,
        windows: false,
        searchAvailable: false));

    False(ShellKeyboardPolicy.ShouldFocusSearch(
        control: false,
        shift: false,
        alt: false,
        windows: false,
        searchAvailable: true));
});

Check("shell Escape shortcut only clears focused non-empty search without modifiers", () =>
{
    True(ShellKeyboardPolicy.ShouldClearSearch(
        escapePressed: true,
        noModifiers: true,
        searchFocused: true,
        hasSearchText: true));

    False(ShellKeyboardPolicy.ShouldClearSearch(
        escapePressed: true,
        noModifiers: true,
        searchFocused: true,
        hasSearchText: false));

    False(ShellKeyboardPolicy.ShouldClearSearch(
        escapePressed: true,
        noModifiers: false,
        searchFocused: true,
        hasSearchText: true));

    False(ShellKeyboardPolicy.ShouldClearSearch(
        escapePressed: true,
        noModifiers: true,
        searchFocused: false,
        hasSearchText: true));
});

Check("shell list activation requires plain Enter", () =>
{
    True(ShellKeyboardPolicy.ShouldActivateListItem(
        enterPressed: true,
        noModifiers: true));

    False(ShellKeyboardPolicy.ShouldActivateListItem(
        enterPressed: true,
        noModifiers: false));

    False(ShellKeyboardPolicy.ShouldActivateListItem(
        enterPressed: false,
        noModifiers: true));
});

Check("shell list copy requires Ctrl+C without extra modifiers", () =>
{
    True(ShellKeyboardPolicy.ShouldCopyListItem(
        cPressed: true,
        control: true,
        shift: false,
        alt: false,
        windows: false));

    False(ShellKeyboardPolicy.ShouldCopyListItem(
        cPressed: true,
        control: true,
        shift: true,
        alt: false,
        windows: false));

    False(ShellKeyboardPolicy.ShouldCopyListItem(
        cPressed: true,
        control: false,
        shift: false,
        alt: false,
        windows: false));

    False(ShellKeyboardPolicy.ShouldCopyListItem(
        cPressed: false,
        control: true,
        shift: false,
        alt: false,
        windows: false));
});

Check("window placement math clamps oversized off-screen windows to the monitor work area", () =>
{
    WindowBounds target = WindowPlacementMath.ClampToWorkArea(
        left: 5000,
        top: 5000,
        width: 2500,
        height: 1400,
        workLeft: -1920,
        workTop: 0,
        workRight: 0,
        workBottom: 1040);

    True(target.Left == -1920);
    True(target.Top == 0);
    True(target.Width == 1920);
    True(target.Height == 1040);
});

Check("window placement math flips summon windows away from bottom-right edges", () =>
{
    WindowBounds target = WindowPlacementMath.PlaceNearCursor(
        cursorX: 1900,
        cursorY: 1000,
        windowWidth: 390,
        windowHeight: 760,
        workLeft: 0,
        workTop: 0,
        workRight: 1920,
        workBottom: 1040,
        gap: 14);

    True(target.Left == 1496);
    True(target.Top == 226);
    True(target.Width == 390);
    True(target.Height == 760);
});

Check("window placement math keeps summon windows inside negative-coordinate monitors", () =>
{
    WindowBounds target = WindowPlacementMath.PlaceNearCursor(
        cursorX: -15,
        cursorY: 20,
        windowWidth: 500,
        windowHeight: 600,
        workLeft: -1600,
        workTop: -200,
        workRight: 0,
        workBottom: 900,
        gap: 14);

    True(target.Left == -529);
    True(target.Top == 34);
    True(target.Left >= -1600);
    True(target.Left + target.Width <= 0);
    True(target.Top >= -200);
    True(target.Top + target.Height <= 900);
});

Check("window placement math canonicalizes invalid native window extents", () =>
{
    WindowBounds target = WindowPlacementMath.ClampToWorkArea(
        left: 100,
        top: 120,
        width: 0,
        height: -20,
        workLeft: 0,
        workTop: 0,
        workRight: 1920,
        workBottom: 1040);

    True(target.Left == 100);
    True(target.Top == 120);
    True(target.Width == 1);
    True(target.Height == 1);
});

Check("window placement persists independently for each layout", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityWindowPlacement-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Preferences.SidebarPlacement = new WindowPlacementState
        {
            HasSize = true,
            HasPosition = true,
            Width = 390,
            Height = 760,
            Left = -1200,
            Top = 40,
        };
        state.Preferences.CompactPlacement = new WindowPlacementState
        {
            HasSize = true,
            Width = 1120,
            Height = 780,
        };
        store.Save(state);

        WorkspaceState loaded = store.Load();
        True(loaded.Preferences.SidebarPlacement.HasSize);
        True(loaded.Preferences.SidebarPlacement.HasPosition);
        True(Math.Abs(loaded.Preferences.SidebarPlacement.Width - 390) < 0.01);
        True(Math.Abs(loaded.Preferences.SidebarPlacement.Left - (-1200)) < 0.01);
        True(loaded.Preferences.CompactPlacement.HasSize);
        True(Math.Abs(loaded.Preferences.CompactPlacement.Width - 1120) < 0.01);
        False(loaded.Preferences.ExpandedPlacement.HasSize);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Check("invalid persisted window size is disabled during normalization", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "JUtilityInvalidPlacement-" + Guid.NewGuid().ToString("N"));
    try
    {
        WorkspaceStore store = new(root);
        WorkspaceState state = store.Load();
        state.Preferences.ExpandedPlacement = new WindowPlacementState
        {
            HasSize = true,
            Width = -5,
            Height = 900,
            HasPosition = true,
            Left = 10,
            Top = 10,
        };
        store.Save(state);

        WorkspaceState loaded = store.Load();
        False(loaded.Preferences.ExpandedPlacement.HasSize);
        True(loaded.Preferences.ExpandedPlacement.HasPosition);
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

Check("claude sessions: AskUserQuestion pending needs you even while fresh", () =>
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-09-25T12:00:00Z");
    TranscriptTail tail = TranscriptTailParser.Parse([
        """{"type":"user","timestamp":"2026-09-25T11:59:00Z","message":{"content":"build it"}}""",
        """{"type":"assistant","timestamp":"2026-09-25T11:59:50Z","message":{"content":[{"type":"tool_use","name":"AskUserQuestion"}]}}""",
    ]);
    Equal("AskUserQuestion", tail.PendingToolName ?? "");
    True(ClaudeSessionClassifier.Classify(tail, now.AddSeconds(-5), now, false).Status == ClaudeSessionStatus.NeedsYou);
});

Check("claude sessions: fresh transcript is working, stale pending tool needs you", () =>
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-09-25T12:00:00Z");
    TranscriptTail tail = TranscriptTailParser.Parse([
        """{"type":"assistant","timestamp":"2026-09-25T11:00:00Z","message":{"content":[{"type":"tool_use","name":"Bash"}]}}""",
    ]);
    True(ClaudeSessionClassifier.Classify(tail, now.AddSeconds(-20), now, false).Status == ClaudeSessionStatus.Working);
    True(ClaudeSessionClassifier.Classify(tail, now.AddMinutes(-2), now, false).Status == ClaudeSessionStatus.Working);
    True(ClaudeSessionClassifier.Classify(tail, now.AddMinutes(-30), now, false).Status == ClaudeSessionStatus.NeedsYou);
});

Check("claude sessions: tool results clear pending tools; questions, done and merged PRs classify", () =>
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-09-25T12:00:00Z");
    DateTimeOffset old = now.AddHours(-1);
    TranscriptTail question = TranscriptTailParser.Parse([
        """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash"}]}}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","content":"ok"}]}}""",
        """{"type":"assistant","timestamp":"2026-09-25T11:00:00Z","message":{"content":[{"type":"text","text":"Build passes.\n\nShould I open the PR?"}]}}""",
    ]);
    True(question.PendingToolName is null);
    True(ClaudeSessionClassifier.Classify(question, old, now, false).Status == ClaudeSessionStatus.NeedsYou);

    TranscriptTail done = TranscriptTailParser.Parse([
        """{"type":"assistant","timestamp":"2026-09-25T11:00:00Z","message":{"content":[{"type":"text","text":"PR merged on green CI."}]}}""",
    ]);
    True(ClaudeSessionClassifier.Classify(done, old, now, false).Status == ClaudeSessionStatus.Done);

    TranscriptTail plain = TranscriptTailParser.Parse([
        """{"type":"assistant","timestamp":"2026-09-25T11:00:00Z","message":{"content":[{"type":"text","text":"Refactored the parser."}]}}""",
        "{\"type\":\"assistant\",\"partial",
    ]);
    True(ClaudeSessionClassifier.Classify(plain, old, now, false).Status == ClaudeSessionStatus.Idle);
    True(ClaudeSessionClassifier.Classify(plain, old, now, true).Status == ClaudeSessionStatus.Done);
});

Check("claude sessions: project names, tiers and desktop metadata mapping", () =>
{
    Equal("datapass-vscode", ClaudeSessionMonitor.ProjectFromFolderName("D--PROJ-datapass-vscode"));
    Equal("diagramcloud", ClaudeSessionMonitor.ProjectFromFolderName("D--PROJ-diagramcloud--claude-worktrees-jovial-williamson-fb5c47"));
    Equal("PowerToy_UI", ClaudeProjectTiers.ProjectNameFromPath(@"D:\PROJ\PowerToy_UI\.claude\worktrees\x"));
    ClaudeProjectTiers tiers = ClaudeProjectTiers.Parse("| D:\\PROJ\\datapass-vscode | max | ask |\n| everything else (atlasnote) | high | ask |");
    Equal("max", tiers.CeilingFor("datapass-vscode"));
    Equal("high", tiers.CeilingFor("atlasnote"));
    ClaudeDesktopSession? meta = ClaudeSessionMonitor.ParseDesktopSession(
        """{"sessionId":"local_abc","cliSessionId":"uuid-1","title":"T","isArchived":true,"prs":[{"prNumber":4,"url":"u","state":"MERGED"}]}""");
    Equal("uuid-1", meta?.CliSessionId ?? "");
    True(meta!.IsArchived && meta.Prs[0].Number == 4);
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
