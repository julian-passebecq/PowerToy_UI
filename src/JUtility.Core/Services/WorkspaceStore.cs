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
        File.WriteAllText(path, JsonSerializer.Serialize(Normalize(CloneState(state)), JsonOptions));
    }

    public WorkspaceState Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);

        using (JsonDocument document = JsonDocument.Parse(json))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.EnumerateObject().Any(property => KnownWorkspaceProperty(property.Name)))
            {
                throw new InvalidDataException("The selected file does not contain recognizable J Utility workspace data.");
            }
        }

        WorkspaceState imported = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions)
            ?? throw new InvalidDataException("The selected file does not contain a valid workspace.");

        WorkspaceState normalized = Normalize(imported);
        Save(normalized);
        return normalized;
    }

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

            state = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions);
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
        if (incomingSchemaVersion > WorkspaceState.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Workspace schema {incomingSchemaVersion} is newer than supported schema {WorkspaceState.CurrentSchemaVersion}. The file was not rewritten.");
        }

        state.Preferences ??= new AppPreferences();
        state.Projects = (state.Projects ?? []).Where(item => item is not null).ToList();
        state.RepositoryLists = (state.RepositoryLists ?? []).Where(item => item is not null).ToList();
        state.Portals = (state.Portals ?? []).Where(item => item is not null).ToList();
        state.Resources = (state.Resources ?? []).Where(item => item is not null).ToList();
        state.ClipboardSnippets = (state.ClipboardSnippets ?? []).Where(item => item is not null).ToList();
        state.PromptModules = (state.PromptModules ?? []).Where(item => item is not null).ToList();
        state.RecentPrompts = (state.RecentPrompts ?? []).Where(item => item is not null).ToList();
        state.Notes = (state.Notes ?? []).Where(item => item is not null).ToList();

        if (incomingSchemaVersion < 2
            && state.Preferences.AlwaysOnTop
            && state.Preferences.WindowBehavior == WindowBehaviorMode.Normal)
        {
            state.Preferences.WindowBehavior = WindowBehaviorMode.AlwaysOnTop;
        }

        state.Preferences.GitHubOwner = NormalizeText(state.Preferences.GitHubOwner, "julian-passebecq");

        // Keep the legacy field synchronized so exported workspaces still round-trip with v1 builds.
        state.Preferences.AlwaysOnTop = state.Preferences.WindowBehavior == WindowBehaviorMode.AlwaysOnTop;
        state.SchemaVersion = WorkspaceState.CurrentSchemaVersion;

        foreach (ProjectEntry project in state.Projects)
        {
            project.Name = NormalizeText(project.Name, "Untitled project");
            project.Category = NormalizeText(project.Category, "Projects");
            project.Subcategory = NormalizeText(project.Subcategory, "Misc");
            project.ExtraLabel = NormalizeText(project.ExtraLabel, "Extra");
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
            portal.Links = (portal.Links ?? []).Where(item => item is not null).ToList();
            foreach (PortalLinkEntry link in portal.Links)
            {
                link.Label = NormalizeText(link.Label, "Link");
            }
        }

        foreach (WorkspaceResourceEntry resource in state.Resources)
        {
            resource.Name = NormalizeText(resource.Name, "Untitled resource");
            resource.Provider = NormalizeText(resource.Provider, "Other");
            resource.Kind = NormalizeText(resource.Kind, "Link");
            resource.Group = NormalizeText(resource.Group, "General");
        }

        foreach (ClipboardSnippetEntry snippet in state.ClipboardSnippets)
        {
            snippet.Title = NormalizeText(snippet.Title, "Untitled snippet");
            snippet.Category = NormalizeText(snippet.Category, "General");
        }

        foreach (StickyNoteEntry note in state.Notes)
        {
            note.Title = NormalizeText(note.Title, note.Kind == CaptureKind.Transcript ? "Transcript" : "Untitled capture");
        }

        foreach (PromptModuleEntry module in state.PromptModules)
        {
            module.Title = NormalizeText(module.Title, "Untitled module");
            module.Category = NormalizeText(module.Category, "General");
        }

        return state;
    }

    private static string NormalizeText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static WorkspaceState CreateSeedState() => new()
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
}
