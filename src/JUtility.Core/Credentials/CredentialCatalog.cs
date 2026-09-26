using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JUtility.Core.Credentials;

// Credentials & IDs: local metadata for the identifiers and secrets the user juggles across Cloudflare,
// MongoDB Atlas, Azure/Fabric, GitHub... Secret VALUES never live here: they are in Windows Credential
// Manager (ISecretVault), addressed by an opaque per-record target. This file holds labels, non-secret
// identifiers, .env locations and key NAMES only. Separate bounded file (credentials-ids.json) so business
// schema v7 and shell schema 1 stay unchanged.

public enum CredentialKind
{
    Id,
    Url,
    Environment,
    Password,
    Token,
    Secret,
}

public sealed class CredentialRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public CredentialKind Kind { get; set; } = CredentialKind.Id;
    public string Service { get; set; } = "";
    public string Project { get; set; } = "";
    // Only for non-secret kinds. Secret kinds keep this empty; their value is in the vault.
    public string Value { get; set; } = "";
    // Non-secret account name that goes with a password or token (e.g. database user). Optional.
    public string UserName { get; set; } = "";
    public string UsedFor { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    // Uncertain items are private by default: only shareable non-secret values ever reach a review export.
    public bool IsShareable { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum EnvKeyState
{
    Unknown,
    Present,
    Empty,
    Missing,
}

public sealed class EnvKeyEntry
{
    public string Name { get; set; } = "";
    // The user expects this key to exist (it stays listed as Missing when the file no longer declares it).
    public bool Expected { get; set; }
    public EnvKeyState State { get; set; } = EnvKeyState.Unknown;
    // Opaque link to a credential record (never the value).
    public Guid? CredentialRef { get; set; }
}

public sealed class EnvFileEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Project { get; set; } = "";
    public string Environment { get; set; } = "local";
    public string Path { get; set; } = "";
    public List<EnvKeyEntry> Keys { get; set; } = [];
    // When the key names/states were last read from disk; null = never inspected.
    public DateTimeOffset? ObservedUtc { get; set; }
    public string ObservationProblem { get; set; } = "";
}

public sealed class CredentialCatalog
{
    [JsonRequired]
    public string Format { get; set; } = CredentialRules.FormatName;
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    // Separates this data folder's vault entries from another --data-dir's entries for the same Windows user.
    public Guid VaultScope { get; set; } = Guid.NewGuid();
    public List<CredentialRecord> Records { get; set; } = [];
    public List<EnvFileEntry> EnvFiles { get; set; } = [];
}

public enum CredentialView
{
    Project,
    Service,
    Type,
}

public sealed record CredentialGroup(string Name, IReadOnlyList<CredentialRecord> Records);

public static class CredentialRules
{
    public const string FormatName = "powerops-credentials";
    public const string VaultPrefix = "PowerOps/Secret/";
    public const int MaxRecords = 2000, MaxEnvFiles = 200, MaxKeysPerFile = 500;
    public const int MaxLabel = 120, MaxShort = 120, MaxValue = 2048, MaxUsedFor = 500, MaxUrl = 2048, MaxPath = 1024, MaxKeyName = 128;
    public const string NoProject = "(no project)", NoService = "(no service)";

    private static readonly Regex KeyName = new("^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

    public static bool IsSecret(CredentialKind kind) => kind is CredentialKind.Password or CredentialKind.Token or CredentialKind.Secret;

    public static string KindLabel(CredentialKind kind) => kind switch
    {
        CredentialKind.Id => "ID",
        CredentialKind.Url => "URL",
        CredentialKind.Environment => "Environment",
        CredentialKind.Password => "Password",
        CredentialKind.Token => "Token",
        _ => "Secret",
    };

    /// <summary>Opaque Credential Manager target for a secret record. Contains no label, service or value.</summary>
    public static string VaultTarget(CredentialCatalog catalog, Guid recordId) => ScopePrefix(catalog) + recordId.ToString("N");

    public static string ScopePrefix(CredentialCatalog catalog) => VaultPrefix + catalog.VaultScope.ToString("N") + "/";

    /// <summary>Opaque reference usable in exports and other apps; resolves only inside this Power Ops data folder.</summary>
    public static string CredentialRef(Guid recordId) => "powerops-credential:" + recordId.ToString("N");

    public static bool IsValidKeyName(string? name) => !string.IsNullOrEmpty(name) && name.Length <= MaxKeyName && KeyName.IsMatch(name);

    public static void Validate(CredentialCatalog catalog)
    {
        if (catalog is null || catalog.Format != FormatName || catalog.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported credentials format/version. Existing files were not rewritten.");
        if (catalog.VaultScope == Guid.Empty) throw new InvalidDataException("Missing vault scope.");
        if (catalog.Records is null || catalog.Records.Count > MaxRecords) throw new InvalidDataException("Invalid credential record collection.");
        if (catalog.EnvFiles is null || catalog.EnvFiles.Count > MaxEnvFiles) throw new InvalidDataException("Invalid .env registry collection.");
        var ids = new HashSet<Guid>();
        foreach (var record in catalog.Records)
        {
            if (record is null || record.Id == Guid.Empty || !ids.Add(record.Id)) throw new InvalidDataException("Missing or duplicate credential record id.");
            ValidateRecord(record);
        }
        var envIds = new HashSet<Guid>();
        foreach (var file in catalog.EnvFiles)
        {
            if (file is null || file.Id == Guid.Empty || !envIds.Add(file.Id)) throw new InvalidDataException("Missing or duplicate .env entry id.");
            ValidateEnvFile(file, ids);
        }
    }

    public static void ValidateRecord(CredentialRecord record)
    {
        if (!Enum.IsDefined(record.Kind)) throw new InvalidDataException("Invalid credential kind.");
        Line(record.Label, MaxLabel, "Label", allowEmpty: false);
        Line(record.Service, MaxShort, "Service");
        Line(record.Project, MaxShort, "Project");
        Line(record.UserName, MaxShort, "User name");
        Text(record.UsedFor, MaxUsedFor, "Used for");
        Line(record.Value, MaxValue, "Value");
        if (IsSecret(record.Kind) && record.Value.Length > 0)
            throw new InvalidDataException($"'{record.Label}' is a {KindLabel(record.Kind)}: its value belongs in Windows Credential Manager, not in the metadata file.");
        if (IsSecret(record.Kind) && record.IsShareable)
            throw new InvalidDataException($"'{record.Label}' is a {KindLabel(record.Kind)} and cannot be marked shareable.");
        Url(record.SourceUrl, "Source / admin URL");
        if (record.Kind == CredentialKind.Url) Url(record.Value, "Value");
    }

    private static void ValidateEnvFile(EnvFileEntry file, HashSet<Guid> recordIds)
    {
        Line(file.Project, MaxShort, ".env project");
        Line(file.Environment, 40, ".env environment label", allowEmpty: false);
        Line(file.ObservationProblem, 300, ".env observation");
        if (string.IsNullOrWhiteSpace(file.Path) || file.Path.Length > MaxPath || !System.IO.Path.IsPathFullyQualified(file.Path) || file.Path.Any(char.IsControl))
            throw new InvalidDataException("A .env entry needs a full local path.");
        if (file.Keys is null || file.Keys.Count > MaxKeysPerFile) throw new InvalidDataException("Too many keys for one .env entry.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in file.Keys)
        {
            if (key is null || !IsValidKeyName(key.Name) || !names.Add(key.Name)) throw new InvalidDataException("Invalid or duplicate .env key name.");
            if (!Enum.IsDefined(key.State)) throw new InvalidDataException("Invalid .env key state.");
            if (key.CredentialRef is Guid reference && !recordIds.Contains(reference))
                throw new InvalidDataException($"{key.Name} links to a credential record that no longer exists.");
        }
    }

    /// <summary>One projection per view; each record appears exactly once. Records are never copied.</summary>
    public static IReadOnlyList<CredentialGroup> Group(IEnumerable<CredentialRecord> records, CredentialView view, string? search = null)
    {
        string term = search?.Trim() ?? "";
        return records
            .Where(x => term.Length == 0 || Matches(x, term))
            .GroupBy(x => view switch
            {
                CredentialView.Project => x.Project.Length == 0 ? NoProject : x.Project,
                CredentialView.Service => x.Service.Length == 0 ? NoService : x.Service,
                _ => KindLabel(x.Kind),
            }, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key is NoProject or NoService ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CredentialGroup(g.Key,
                g.OrderBy(x => x.SortOrder).ThenBy(x => x.Service, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    private static bool Matches(CredentialRecord record, string term)
    {
        bool Has(string value) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
        return Has(record.Label) || Has(record.Service) || Has(record.Project) || Has(record.UsedFor) || Has(record.UserName)
            || Has(KindLabel(record.Kind)) || (!IsSecret(record.Kind) && Has(record.Value));
    }

    /// <summary>Removes the record and every .env link to it. Returns false when the record did not exist.</summary>
    public static bool RemoveRecord(CredentialCatalog catalog, Guid recordId)
    {
        int removed = catalog.Records.RemoveAll(x => x.Id == recordId);
        foreach (var key in catalog.EnvFiles.SelectMany(x => x.Keys).Where(x => x.CredentialRef == recordId)) key.CredentialRef = null;
        return removed > 0;
    }

    /// <summary>Vault targets under this data folder's scope that no record points to (e.g. a record deleted while keeping its secret).</summary>
    public static IReadOnlyList<string> OrphanTargets(CredentialCatalog catalog, IEnumerable<string> vaultTargets)
    {
        string prefix = ScopePrefix(catalog);
        var expected = catalog.Records.Where(x => IsSecret(x.Kind)).Select(x => VaultTarget(catalog, x.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return vaultTargets.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !expected.Contains(x)).ToList();
    }

    private static void Line(string? value, int max, string field, bool allowEmpty = true)
    {
        if (value is null || value.Length > max || value.Any(char.IsControl) || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException(allowEmpty
                ? $"{field}: single line, up to {max} characters."
                : $"{field} is required (single line, up to {max} characters).");
    }

    private static void Text(string? value, int max, string field)
    {
        if (value is null || value.Length > max || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new InvalidDataException($"{field}: up to {max} characters.");
    }

    private static void Url(string? value, string field)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (value.Length > MaxUrl || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException($"{field} must be an http(s) address.");
    }
}

/// <summary>Starter rows for the services the user works with. Values start empty; nothing is fetched.</summary>
public static class CredentialTemplates
{
    public sealed record Row(string Label, CredentialKind Kind, string UsedFor);
    public sealed record Template(string Service, string SourceUrl, IReadOnlyList<Row> Rows);

    public static readonly IReadOnlyList<Template> All = Array.AsReadOnly(new[]
    {
        new Template("Cloudflare", "https://dash.cloudflare.com/",
        [
            new("Account ID", CredentialKind.Id, "API calls, Wrangler account_id"),
            new("Zone ID", CredentialKind.Id, "DNS / zone API calls"),
            new("Access Application ID", CredentialKind.Id, "Zero Trust Access policies"),
            new("Service Token Client ID", CredentialKind.Id, "CF-Access-Client-Id header"),
            new("Service Token Client Secret", CredentialKind.Secret, "CF-Access-Client-Secret header"),
            new("Worker name", CredentialKind.Id, "wrangler deploy target"),
            new("API token", CredentialKind.Token, "CLOUDFLARE_API_TOKEN"),
        ]),
        new Template("MongoDB Atlas", "https://cloud.mongodb.com/",
        [
            new("Organization ID", CredentialKind.Id, "Atlas Admin API"),
            new("Project ID", CredentialKind.Id, "Atlas Admin API (group id)"),
            new("Cluster name", CredentialKind.Id, "Connection string host / Admin API"),
            new("Database name", CredentialKind.Id, "Application database"),
            new("Database user password", CredentialKind.Password, "MONGODB_URI credentials"),
        ]),
        new Template("Azure / Fabric", "https://portal.azure.com/",
        [
            new("Tenant ID", CredentialKind.Id, "Entra ID sign-in"),
            new("Subscription ID", CredentialKind.Id, "Resource management"),
            new("Fabric workspace ID", CredentialKind.Id, "Fabric REST API"),
            new("App (client) ID", CredentialKind.Id, "Service principal"),
            new("Client secret", CredentialKind.Secret, "Service principal secret"),
        ]),
        new Template("GitHub", "https://github.com/settings/tokens",
        [
            new("Owner / organization", CredentialKind.Id, "Repository owner"),
            new("Repository", CredentialKind.Id, "owner/name"),
            new("Environment name", CredentialKind.Environment, "Actions deployment environment"),
            new("Personal access token", CredentialKind.Token, "GH_TOKEN / API"),
        ]),
        new Template("Databricks", "https://accounts.cloud.databricks.com/",
        [
            new("Workspace URL", CredentialKind.Url, "DATABRICKS_HOST"),
            new("Workspace ID", CredentialKind.Id, "Account API"),
            new("Personal access token", CredentialKind.Token, "DATABRICKS_TOKEN"),
        ]),
        new Template("Vercel", "https://vercel.com/dashboard",
        [
            new("Team ID", CredentialKind.Id, "VERCEL_ORG_ID"),
            new("Project ID", CredentialKind.Id, "VERCEL_PROJECT_ID"),
            new("Access token", CredentialKind.Token, "VERCEL_TOKEN"),
        ]),
        new Template("Netlify", "https://app.netlify.com/",
        [
            new("Site ID", CredentialKind.Id, "NETLIFY_SITE_ID"),
            new("Personal access token", CredentialKind.Token, "NETLIFY_AUTH_TOKEN"),
        ]),
    });

    /// <summary>Adds the template's rows for a project; rows whose label already exists for that service and project are skipped.</summary>
    public static IReadOnlyList<CredentialRecord> Apply(CredentialCatalog catalog, Template template, string project)
    {
        project = project.Trim();
        var added = new List<CredentialRecord>();
        int order = catalog.Records.Count == 0 ? 0 : catalog.Records.Max(x => x.SortOrder) + 1;
        foreach (var row in template.Rows)
        {
            bool exists = catalog.Records.Any(x => string.Equals(x.Service, template.Service, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Project, project, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Label, row.Label, StringComparison.OrdinalIgnoreCase));
            if (exists) continue;
            if (catalog.Records.Count >= CredentialRules.MaxRecords) throw new InvalidOperationException($"Credential record limit ({CredentialRules.MaxRecords}) reached.");
            var record = new CredentialRecord
            {
                Label = row.Label, Kind = row.Kind, Service = template.Service, Project = project,
                UsedFor = row.UsedFor, SourceUrl = template.SourceUrl, SortOrder = order++,
            };
            catalog.Records.Add(record);
            added.Add(record);
        }
        return added;
    }
}

public sealed class CredentialCatalogStore
{
    public const int MaxBytes = 1024 * 1024;
    public string FilePath { get; }

    public CredentialCatalogStore(string directory) => FilePath = Path.Combine(Path.GetFullPath(directory), "credentials-ids.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static CredentialCatalog Copy(CredentialCatalog catalog) =>
        JsonSerializer.Deserialize<CredentialCatalog>(JsonSerializer.SerializeToUtf8Bytes(catalog, Json), Json)!;

    /// <summary>Missing file = empty catalog (nothing written). Malformed/future files fail closed and stay untouched.</summary>
    public CredentialCatalog Load() => File.Exists(FilePath) ? Read(FilePath) : new CredentialCatalog();

    public static CredentialCatalog Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Credentials JSON exceeds 1 MiB.");
        var catalog = JsonSerializer.Deserialize<CredentialCatalog>(stream, Json) ?? throw new InvalidDataException("Empty credentials JSON.");
        CredentialRules.Validate(catalog);
        return catalog;
    }

    public void Save(CredentialCatalog catalog)
    {
        CredentialRules.Validate(catalog);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Credentials JSON exceeds 1 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(FilePath)) _ = Read(FilePath); // Never overwrite unsupported/corrupt bytes.
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".backup", true);
            else File.Move(temporary, FilePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>
    /// Saves twice so the prior-generation .backup no longer holds a value that was just removed
    /// (used after moving a mistakenly typed secret out of the metadata file into the vault).
    /// </summary>
    public void SaveAndRotateBackup(CredentialCatalog catalog)
    {
        Save(catalog);
        Save(catalog);
    }
}
