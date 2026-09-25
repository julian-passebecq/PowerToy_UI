using System.Text.Json;
using System.Text.Json.Nodes;
using JUtility.Core.Credentials;
using JUtility.Core.Models;

namespace JUtility.Core.Workspaces;

/// <summary>Review/share format, deliberately distinct from a restorable workspace backup.</summary>
public static class PortableExport
{
    public static string Create(WorkspaceState source, ShellState shell, IEnumerable<string> selected, bool includeLocalDetails = false, bool includeShell = false, CredentialCatalog? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        WorkspaceSessions.Validate(shell);
        var ids = selected.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) throw new InvalidDataException("Select at least one module to export.");
        var modules = new JsonObject();
        foreach (string id in ids)
        {
            ModuleCatalog.Get(id);
            modules[id] = id switch
            {
                "repositories" => Node(new { source.Projects, source.RepositoryLists }),
                "portals" => Node(source.Portals),
                "resources" => Node(source.Resources),
                "capture" => Node(source.Notes),
                "clipboard" => Node(new
                {
                    Text = source.ClipboardSnippets,
                    Media = source.ClipboardMedia.Select(x => new
                    { x.Id, x.Kind, x.Title, x.Category, x.Tags, x.ProjectId, x.RelativePath, x.MimeType, x.ByteLength, x.IsPinned })
                }),
                "prompts" => Node(new { source.PromptModules, source.RecentPrompts }),
                "tools" => includeLocalDetails ? Node(source.Tools) : Node(source.Tools.Select(x => new { x.Id, x.Name, x.Category, x.IsPinned, x.SortOrder, LocalLaunchDetailsOmitted = true })),
                "settings" => includeLocalDetails ? Node(new { source.Preferences, source.ExplorerFolders }) : Node(new { ExplorerFolders = source.ExplorerFolders.Select(x => new { x.Id, x.Name, x.IsPinned }), LocalPathsAndPreferencesOmitted = true }),
                "credentials" => CredentialsNode(credentials, includeLocalDetails),
                // Tray items are local files and file-tray.json holds machine paths: neither is exported, even with local details.
                "tray" => Node(new { Observation = "File tray items and settings are local to this computer and are never exported." }),
                "features" => Node(ModuleCatalog.All),
                "home" or "dashboard" => Node(new { Repositories = source.Projects.Count, Captures = source.Notes.Count, Media = source.ClipboardMedia.Count }),
                _ => Node(new { Observation = "No runtime snapshot exported; open the module and inspect locally." })
            };
        }
        var root = new JsonObject
        {
            ["format"] = "powerops-content-export",
            ["schemaVersion"] = 1,
            ["sourceWorkspaceSchemaVersion"] = source.SchemaVersion,
            ["exportedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["containsMediaBytes"] = false,
            ["containsLocalLaunchDetails"] = includeLocalDetails,
            ["warning"] = "Selected content may contain private text or secrets supplied by the user. Review before sharing. This is not a complete recovery backup and is not accepted by workspace Import.",
            ["modules"] = modules
        };
        if (includeShell) root["shell"] = Node(shell);
        return root.ToJsonString(WorkspaceSessions.Json);
    }
    // Secret values are never available here (they live in the OS vault). Private IDs export without their value;
    // secrets export as an opaque credentialRef. .env paths are machine details and follow includeLocalDetails.
    private static JsonNode? CredentialsNode(CredentialCatalog? catalog, bool includeLocalDetails)
    {
        if (catalog is null) return Node(new { Observation = "Credentials & IDs were not loaded; nothing exported." });
        return Node(new
        {
            SecretValuesIncluded = false,
            Records = catalog.Records.Select(x => new
            {
                x.Id, Kind = CredentialRules.KindLabel(x.Kind), x.Label, x.Service, x.Project, x.UsedFor, x.SourceUrl, x.IsShareable,
                Value = !CredentialRules.IsSecret(x.Kind) && x.IsShareable ? x.Value : null,
                ValueOmitted = CredentialRules.IsSecret(x.Kind) ? "secret: stays in Windows Credential Manager" : x.IsShareable ? null : "private",
                CredentialRef = CredentialRules.IsSecret(x.Kind) ? CredentialRules.CredentialRef(x.Id) : null,
            }),
            EnvFiles = catalog.EnvFiles.Select(x => new
            {
                x.Id, x.Project, x.Environment,
                Path = includeLocalDetails ? x.Path : null,
                FileName = Path.GetFileName(x.Path),
                x.ObservedUtc,
                Keys = x.Keys.Select(k => new { k.Name, k.Expected, State = k.State.ToString(), CredentialRef = k.CredentialRef is Guid id ? CredentialRules.CredentialRef(id) : null }),
            }),
        });
    }
    private static JsonNode? Node(object value) => JsonSerializer.SerializeToNode(value, WorkspaceSessions.Json);
}
