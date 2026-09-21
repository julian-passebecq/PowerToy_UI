using System.Text.Json;
using JUtility.Core.Models;

namespace JUtility.Core.Services;

public sealed class WorkspaceStore
{
    private const int MaxRecentPrompts = 30;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public WorkspaceStore(string? directory = null)
    {
        DataDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JUtilityPalette");

        Directory.CreateDirectory(DataDirectory);
        DataFilePath = Path.Combine(DataDirectory, "workspace.json");
        BackupFilePath = Path.Combine(DataDirectory, "workspace.backup.json");
    }

    public string DataDirectory { get; }
    public string DataFilePath { get; }
    public string BackupFilePath { get; }

    public WorkspaceState Load()
    {
        bool primaryExists = File.Exists(DataFilePath);
        bool backupExists = File.Exists(BackupFilePath);

        if (primaryExists)
        {
            if (TryLoad(DataFilePath, out WorkspaceState? state))
            {
                return Normalize(state!);
            }

            if (backupExists && TryLoad(BackupFilePath, out WorkspaceState? backup))
            {
                WorkspaceState recovered = Normalize(backup!);
                PreserveInvalidPrimary();
                WritePrimaryWithoutReplacingBackup(recovered);
                return recovered;
            }

            throw new InvalidDataException(
                "The primary workspace exists but is malformed or unrecognized, and no valid backup is available. The file was preserved and was not replaced.");
        }

        if (backupExists)
        {
            if (TryLoad(BackupFilePath, out WorkspaceState? backup))
            {
                WorkspaceState recovered = Normalize(backup!);
                WritePrimaryWithoutReplacingBackup(recovered);
                return recovered;
            }

            throw new InvalidDataException(
                "The workspace backup exists but is malformed or unrecognized. A new workspace was not created over it.");
        }

        WorkspaceState seeded = CreateSeedState();
        Save(seeded);
        return seeded;
    }

    public void Save(WorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        WorkspaceState normalized = Normalize(CloneState(state));

        string tempPath = CreateTemporaryPath("save");
        try
        {
            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(tempPath, json);

            if (File.Exists(DataFilePath))
            {
                if (TryLoad(DataFilePath, out WorkspaceState? currentPrimary))
                {
                    // Validate the existing generation before promoting it to last-known-good backup.
                    Normalize(currentPrimary!);
                    File.Copy(DataFilePath, BackupFilePath, overwrite: true);
                }
                else
                {
                    // Never rotate malformed bytes over a usable backup.
                    PreserveInvalidPrimary();
                }
            }

            File.Move(tempPath, DataFilePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }
    }

    private void WritePrimaryWithoutReplacingBackup(WorkspaceState state)
    {
        string tempPath = CreateTemporaryPath("recovery");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tempPath, DataFilePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }
    }

    private string PreserveInvalidPrimary()
    {
        string recoveryPath = Path.Combine(
            DataDirectory,
            $"workspace.invalid.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");
        File.Copy(DataFilePath, recoveryPath, overwrite: false);
        return recoveryPath;
    }

    private string CreateTemporaryPath(string operation) =>
        Path.Combine(DataDirectory, $"workspace.{operation}.{Guid.NewGuid():N}.tmp");

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A stale temp file is non-authoritative; leave it for later cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above: never turn cleanup failure into loss of the committed workspace.
        }
    }

    private static WorkspaceState CloneState(WorkspaceState state)
    {
        string json = JsonSerializer.Serialize(state, JsonOptions);
        return JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions)
            ?? throw new InvalidDataException("The workspace could not be cloned for persistence.");
    }

    public void Export(WorkspaceState state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string destination = Path.GetFullPath(path);
        if (PathsEqual(destination, DataFilePath) || PathsEqual(destination, BackupFilePath))
        {
            throw new InvalidOperationException("Export cannot target the active workspace primary or backup file.");
        }

        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException("The export destination directory does not exist.");
        }

        WorkspaceState normalized = Normalize(CloneState(state));
        string tempPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(tempPath, destination, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    public WorkspaceState PrepareImport(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        bool hasExplicitSchema;

        using (JsonDocument document = JsonDocument.Parse(json))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.EnumerateObject().Any(property => KnownWorkspaceProperty(property.Name)))
            {
                throw new InvalidDataException("The selected file does not contain recognizable J Utility workspace data.");
            }

            hasExplicitSchema = HasWorkspaceProperty(document.RootElement, nameof(WorkspaceState.SchemaVersion));
        }

        WorkspaceState imported = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions)
            ?? throw new InvalidDataException("The selected file does not contain a valid workspace.");

        if (!hasExplicitSchema)
        {
            imported.SchemaVersion = 1;
        }

        return Normalize(imported);
    }

    public WorkspaceState CommitImport(WorkspaceState candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        WorkspaceState detached = Normalize(CloneState(candidate));
        Save(detached);
        return detached;
    }

    public WorkspaceState Import(string path) =>
        CommitImport(PrepareImport(path));

    private static bool KnownWorkspaceProperty(string name) =>
        name.Equals(nameof(WorkspaceState.SchemaVersion), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.Preferences), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.Projects), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.RepositoryLists), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.Portals), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.Resources), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.ClipboardSnippets), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.PromptModules), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.RecentPrompts), StringComparison.OrdinalIgnoreCase)
        || name.Equals(nameof(WorkspaceState.Notes), StringComparison.OrdinalIgnoreCase);

    private static bool HasWorkspaceProperty(JsonElement root, string propertyName) =>
        root.EnumerateObject().Any(property =>
            property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

    public static void AddRecentPrompt(WorkspaceState state, string title, string text)
    {
        string normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        state.RecentPrompts.RemoveAll(item => string.Equals(item.Text, normalized, StringComparison.Ordinal));
        state.RecentPrompts.Add(new RecentPromptEntry
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Prompt" : title.Trim(),
            Text = normalized,
            CreatedUtc = DateTimeOffset.UtcNow,
        });

        foreach (RecentPromptEntry stale in state.RecentPrompts
            .OrderByDescending(item => item.CreatedUtc)
            .Skip(MaxRecentPrompts)
            .ToArray())
        {
            state.RecentPrompts.Remove(stale);
        }
    }

    private static bool TryLoad(string path, out WorkspaceState? state)
    {
        state = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.EnumerateObject().Any(property => KnownWorkspaceProperty(property.Name)))
            {
                return false;
            }

            bool hasExplicitSchema = HasWorkspaceProperty(document.RootElement, nameof(WorkspaceState.SchemaVersion));
            state = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions);
            if (state is not null && !hasExplicitSchema)
            {
                state.SchemaVersion = 1;
            }

            return state is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static WorkspaceState Normalize(WorkspaceState state)
    {
        int incomingSchemaVersion = state.SchemaVersion;
        if (incomingSchemaVersion < 1)
        {
            throw new InvalidDataException($"Workspace schema {incomingSchemaVersion} is invalid. The file was not rewritten.");
        }

        if (incomingSchemaVersion > WorkspaceState.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Workspace schema {incomingSchemaVersion} is newer than supported schema {WorkspaceState.CurrentSchemaVersion}. The file was not rewritten.");
        }

        state.Preferences ??= new AppPreferences();
        state.Preferences.SidebarPlacement ??= new WindowPlacementState();
        state.Preferences.CompactPlacement ??= new WindowPlacementState();
        state.Preferences.ExpandedPlacement ??= new WindowPlacementState();
        NormalizeWindowPlacement(state.Preferences.SidebarPlacement);
        NormalizeWindowPlacement(state.Preferences.CompactPlacement);
        NormalizeWindowPlacement(state.Preferences.ExpandedPlacement);
        state.Projects = (state.Projects ?? []).Where(item => item is not null).ToList();
        state.RepositoryLists = (state.RepositoryLists ?? []).Where(item => item is not null).ToList();
        state.Portals = (state.Portals ?? []).Where(item => item is not null).ToList();
        state.Resources = (state.Resources ?? []).Where(item => item is not null).ToList();
        state.ClipboardSnippets = (state.ClipboardSnippets ?? []).Where(item => item is not null).ToList();
        state.PromptModules = (state.PromptModules ?? []).Where(item => item is not null).ToList();
        state.RecentPrompts = (state.RecentPrompts ?? []).Where(item => item is not null).ToList();
        state.Notes = (state.Notes ?? []).Where(item => item is not null).ToList();

        ValidateEnum(state.Preferences.LastView, "Preferences.LastView");
        ValidateEnum(state.Preferences.WindowBehavior, "Preferences.WindowBehavior");
        ValidateEnum(state.Preferences.SummonMouseBinding, "Preferences.SummonMouseBinding");

        ValidateUniqueIds(state.Projects, item => item.Id, "Projects");
        ValidateUniqueIds(state.RepositoryLists, item => item.Id, "RepositoryLists");
        ValidateUniqueIds(state.Portals, item => item.Id, "Portals");
        ValidateUniqueIds(state.Resources, item => item.Id, "Resources");
        ValidateUniqueIds(state.ClipboardSnippets, item => item.Id, "ClipboardSnippets");
        ValidateUniqueIds(state.PromptModules, item => item.Id, "PromptModules");
        ValidateUniqueIds(state.RecentPrompts, item => item.Id, "RecentPrompts");
        ValidateUniqueIds(state.Notes, item => item.Id, "Notes");

        foreach (RepositoryListEntry list in state.RepositoryLists)
        {
            foreach (RepositoryListItemEntry item in list.Items)
            {
                if (item.ProjectId == Guid.Empty)
                {
                    throw new InvalidDataException($"RepositoryLists '{list.Name}' contains an empty ProjectId.");
                }
            }
        }

        foreach (PortalEntry portal in state.Portals)
        {
            ValidateUniqueIds(portal.Links, item => item.Id, $"Portals[{portal.Name}].Links");
        }

        foreach (WorkspaceResourceEntry resource in state.Resources)
        {
            if (resource.SourceProjectId == Guid.Empty)
            {
                throw new InvalidDataException($"Resource '{resource.Name}' contains an empty SourceProjectId.");
            }
        }

        foreach (StickyNoteEntry note in state.Notes)
        {
            ValidateEnum(note.Kind, $"Notes[{note.Title}].Kind");
            if (note.ProjectId == Guid.Empty)
            {
                throw new InvalidDataException($"Capture '{note.Title}' contains an empty ProjectId.");
            }
        }

        if (incomingSchemaVersion < 2
            && state.Preferences.AlwaysOnTop
            && state.Preferences.WindowBehavior == WindowBehaviorMode.Normal)
        {
            state.Preferences.WindowBehavior = WindowBehaviorMode.AlwaysOnTop;
        }

        state.Preferences.GitHubOwner = NormalizeText(state.Preferences.GitHubOwner, "julian-passebecq");
        state.Preferences.LastModule = NormalizeText(state.Preferences.LastModule, "Dashboard");

        // Keep the legacy field synchronized so exported workspaces still round-trip with v1 builds.
        state.Preferences.AlwaysOnTop = state.Preferences.WindowBehavior == WindowBehaviorMode.AlwaysOnTop;
        state.SchemaVersion = WorkspaceState.CurrentSchemaVersion;

        foreach (ProjectEntry project in state.Projects)
        {
            project.Name = NormalizeText(project.Name, "Untitled project");
            project.Category = NormalizeText(project.Category, "Projects");
            project.Subcategory = NormalizeText(project.Subcategory, "Misc");
            project.Note = NullToEmpty(project.Note);
            project.GitHubFullName = NormalizeOptionalSingleLine(project.GitHubFullName);
            project.Language = NormalizeOptionalSingleLine(project.Language);
            project.RepoUrl = NormalizeOptionalSingleLine(project.RepoUrl);
            project.SiteUrl = NormalizeOptionalSingleLine(project.SiteUrl);
            project.ServerUrl = NormalizeOptionalSingleLine(project.ServerUrl);
            project.ChatGptUrl = NormalizeOptionalSingleLine(project.ChatGptUrl);
            project.ExtraLabel = NormalizeText(project.ExtraLabel, "Extra");
            project.ExtraUrl = NormalizeOptionalSingleLine(project.ExtraUrl);
        }

        foreach (RepositoryListEntry list in state.RepositoryLists)
        {
            list.Name = NormalizeText(list.Name, "Untitled list");
            list.Category = NormalizeText(list.Category, "Saved lists");
            list.Items = (list.Items ?? []).Where(item => item is not null).ToList();
        }

        foreach (PortalEntry portal in state.Portals)
        {
            portal.Name = NormalizeText(portal.Name, "Untitled portal");
            portal.Category = NormalizeText(portal.Category, "General");
            portal.IconKey = NormalizeText(portal.IconKey, "↗");
            portal.MainUrl = NormalizeOptionalSingleLine(portal.MainUrl);
            portal.Links = (portal.Links ?? []).Where(item => item is not null).ToList();
            foreach (PortalLinkEntry link in portal.Links)
            {
                link.Label = NormalizeText(link.Label, "Link");
                link.Url = NormalizeOptionalSingleLine(link.Url);
                link.Project = NormalizeOptionalSingleLine(link.Project);
                link.Note = NullToEmpty(link.Note);
            }
        }

        foreach (WorkspaceResourceEntry resource in state.Resources)
        {
            resource.Name = NormalizeText(resource.Name, "Untitled resource");
            resource.Provider = NormalizeText(resource.Provider, "Other");
            resource.Kind = NormalizeText(resource.Kind, "Link");
            resource.Group = NormalizeText(resource.Group, "General");
            resource.Url = NormalizeOptionalSingleLine(resource.Url);
            resource.Note = NullToEmpty(resource.Note);
        }

        foreach (ClipboardSnippetEntry snippet in state.ClipboardSnippets)
        {
            snippet.Title = NormalizeText(snippet.Title, "Untitled snippet");
            snippet.Category = NormalizeText(snippet.Category, "General");
            snippet.Text = NullToEmpty(snippet.Text);
            snippet.Tags = NormalizeOptionalSingleLine(snippet.Tags);
        }

        foreach (StickyNoteEntry note in state.Notes)
        {
            note.Title = NormalizeText(note.Title, note.Kind == CaptureKind.Transcript ? "Transcript" : "Untitled capture");
            note.Subject = NormalizeOptionalSingleLine(note.Subject);
            note.Text = NullToEmpty(note.Text);
            note.Url = NormalizeOptionalSingleLine(note.Url);
            note.Labels = NormalizeOptionalSingleLine(note.Labels);
            note.Status = NormalizeOptionalSingleLine(note.Status);
            note.Priority = NormalizeOptionalSingleLine(note.Priority);
        }

        foreach (PromptModuleEntry module in state.PromptModules)
        {
            module.Title = NormalizeText(module.Title, "Untitled module");
            module.Category = NormalizeText(module.Category, "General");
            module.Body = NullToEmpty(module.Body);
        }

        foreach (RecentPromptEntry prompt in state.RecentPrompts)
        {
            prompt.Title = NormalizeText(prompt.Title, "Prompt");
            prompt.Text = prompt.Text?.Trim() ?? string.Empty;
        }

        state.RecentPrompts = state.RecentPrompts
            .Where(prompt => prompt.Text.Length > 0)
            .GroupBy(prompt => prompt.Text, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(prompt => prompt.CreatedUtc).First())
            .OrderByDescending(prompt => prompt.CreatedUtc)
            .Take(MaxRecentPrompts)
            .ToList();

        return state;
    }

    private static void NormalizeWindowPlacement(WindowPlacementState placement)
    {
        if (!placement.HasSize
            || !double.IsFinite(placement.Width)
            || !double.IsFinite(placement.Height)
            || placement.Width <= 0
            || placement.Height <= 0)
        {
            placement.HasSize = false;
            placement.Width = 0;
            placement.Height = 0;
        }
        else
        {
            placement.Width = Math.Clamp(placement.Width, 360, 10000);
            placement.Height = Math.Clamp(placement.Height, 400, 10000);
        }

        if (!placement.HasPosition
            || !double.IsFinite(placement.Left)
            || !double.IsFinite(placement.Top))
        {
            placement.HasPosition = false;
            placement.Left = 0;
            placement.Top = 0;
        }
    }

    private static void ValidateUniqueIds<T>(
        IEnumerable<T> items,
        Func<T, Guid> idSelector,
        string collectionName)
    {
        HashSet<Guid> seen = [];
        int index = 0;
        foreach (T item in items)
        {
            Guid id = idSelector(item);
            if (id == Guid.Empty)
            {
                throw new InvalidDataException($"{collectionName}[{index}] has an empty Id.");
            }

            if (!seen.Add(id))
            {
                throw new InvalidDataException($"{collectionName} contains duplicate Id '{id}'.");
            }

            index++;
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, string field)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new InvalidDataException($"{field} contains unsupported value '{value}'.");
        }
    }

    private static string NormalizeText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeOptionalSingleLine(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NullToEmpty(string? value) => value ?? string.Empty;

    private static WorkspaceState CreateSeedState()
    {
        WorkspaceState state = new()
    {
        Projects =
        [
            new ProjectEntry
            {
                Name = "VisualAlgo",
                Category = "Fluent2 J Consumers",
                Note = "Visual algorithms consumer: repository + deployed validation site.",
                RepoUrl = "https://github.com/julian-passebecq/Fluent2_J_VisualAlgo",
                SiteUrl = "https://fluent2jvisualalgo.netlify.app/",
            },
            new ProjectEntry
            {
                Name = "CloudArchi",
                Category = "Fluent2 J Consumers",
                Note = "Cloud architecture consumer: repository + deployed validation site.",
                RepoUrl = "https://github.com/julian-passebecq/Fluent2_J_CloudArchi",
                SiteUrl = "https://f2jcloudarchi.netlify.app/",
            },
        ],
        PromptModules =
        [
            new PromptModuleEntry
            {
                Title = "Base Prompt",
                Category = "Core",
                SortOrder = 10,
                Body = "Work on the current project. Preserve working behavior and make focused changes that directly improve the requested workflow.",
            },
            new PromptModuleEntry
            {
                Title = "Audit",
                Category = "Development",
                SortOrder = 20,
                Body = "Audit the implementation before editing. Identify concrete defects, unnecessary complexity, and missing tests.",
            },
            new PromptModuleEntry
            {
                Title = "Debug",
                Category = "Development",
                SortOrder = 30,
                Body = "Reproduce failures where possible, fix the root cause, and verify the critical paths after the change.",
            },
            new PromptModuleEntry
            {
                Title = "Current Sources",
                Category = "Research",
                SortOrder = 40,
                Body = "When facts may have changed, verify them against current authoritative sources before making implementation decisions.",
                IsEnabled = false,
            },
            new PromptModuleEntry
            {
                Title = "Output",
                Category = "Format",
                SortOrder = 50,
                Body = "At the end, summarize what changed, what was tested, and any remaining limitation in concise technical language.",
            },
        ],
        Notes =
        [
            new StickyNoteEntry
            {
                Title = "J Utility",
                Text = "Keep this tool small: prompts, project links, and temporary notes first.",
                IsPinned = true,
            },
        ],
    };

        StarterCatalogService.Merge(state.Portals, state.ClipboardSnippets);
        return state;
    }
}
