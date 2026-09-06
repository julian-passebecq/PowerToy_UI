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
        if (TryLoad(DataFilePath, out WorkspaceState? state))
        {
            return Normalize(state!);
        }

        if (TryLoad(BackupFilePath, out WorkspaceState? backup))
        {
            WorkspaceState recovered = Normalize(backup!);
            Save(recovered);
            return recovered;
        }

        WorkspaceState seeded = CreateSeedState();
        Save(seeded);
        return seeded;
    }

    public void Save(WorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        WorkspaceState normalized = Normalize(state);

        string tempPath = DataFilePath + ".tmp";
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(tempPath, json);

        if (File.Exists(DataFilePath))
        {
            File.Copy(DataFilePath, BackupFilePath, overwrite: true);
        }

        File.Move(tempPath, DataFilePath, overwrite: true);
    }

    public void Export(WorkspaceState state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, JsonSerializer.Serialize(Normalize(state), JsonOptions));
    }

    public WorkspaceState Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        WorkspaceState imported = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions)
            ?? throw new InvalidDataException("The selected file does not contain a valid workspace.");

        WorkspaceState normalized = Normalize(imported);
        Save(normalized);
        return normalized;
    }

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

            state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(path), JsonOptions);
            return state is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static WorkspaceState Normalize(WorkspaceState state)
    {
        state.SchemaVersion = WorkspaceState.CurrentSchemaVersion;
        state.Preferences ??= new AppPreferences();
        state.Projects ??= [];
        state.PromptModules ??= [];
        state.RecentPrompts ??= [];
        state.Notes ??= [];

        foreach (ProjectEntry project in state.Projects)
        {
            project.Name = NormalizeText(project.Name, "Untitled project");
            project.Category = NormalizeText(project.Category, "Projects");
            project.ExtraLabel = NormalizeText(project.ExtraLabel, "Extra");
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
