using System.Diagnostics;
using System.IO;

namespace JUtility.App.Services;

public sealed record EnvironmentPathEntry(string Tool, string Category, string Path, string FileVersion, string Evidence);

/// <summary>Explicit, bounded PATH metadata scan. Does not run discovered programs or read secrets.</summary>
public static class EnvironmentPathInventory
{
    private static readonly (string Name, string Category)[] Tools =
    [
        ("git", "Git"), ("gh", "Git"), ("python", "Python"), ("python3", "Python"), ("py", "Python"),
        ("conda", "Python"), ("uv", "Python"), ("jupyter", "Python"),
        ("node", "Runtimes"), ("npm", "Runtimes"), ("R", "Runtimes"), ("Rscript", "Runtimes"),
        ("dotnet", "Runtimes"), ("java", "Runtimes"), ("docker", "Cloud"), ("kubectl", "Cloud"),
        ("aws", "Cloud"), ("az", "Cloud"), ("gcloud", "Cloud"), ("oci", "Cloud"),
        ("pwsh", "Terminals"), ("powershell", "Terminals"), ("cmd", "Terminals"),
        ("wt", "Terminals"), ("wsl", "Terminals"), ("bash", "Terminals"),
        ("code", "Editors & agents"), ("codex", "Editors & agents"), ("claude", "Editors & agents")
    ];
    public static IReadOnlyList<EnvironmentPathEntry> Scan(CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directories.Count >= 80) break;
            try
            {
                string path = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || path.StartsWith("//")) continue;
                string root = Path.GetPathRoot(path)!;
                if (new DriveInfo(root).DriveType != DriveType.Fixed) continue;
                if (!directories.Contains(path, StringComparer.OrdinalIgnoreCase)) directories.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { /* Unavailable PATH entry, not a fatal scan failure. */ }
        }
        var rows = new List<EnvironmentPathEntry>();
        foreach (var tool in Tools)
        {
            bool found = false;
            foreach (var directory in directories)
            foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                string candidate = Path.Combine(directory, tool.Name + extension);
                if (!File.Exists(candidate)) continue;
                found = true;
                string version = "Not available";
                try
                {
                    if (extension == ".exe") version = FileVersionInfo.GetVersionInfo(candidate).ProductVersion ?? "Not available";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                rows.Add(new(tool.Name, tool.Category, candidate, version, "Local PATH metadata; not a runtime version probe"));
            }
            if (!found) rows.Add(new(tool.Name, tool.Category, "Not found on local PATH", "Unknown", "May still be installed elsewhere / inside WSL or a virtual environment"));
        }
        return rows;
    }
}
