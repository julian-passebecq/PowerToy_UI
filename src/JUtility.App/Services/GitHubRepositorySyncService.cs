using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JUtility.App.Services;

public sealed record GitHubRepositorySnapshot(
    string Name,
    string FullName,
    string Url,
    string Description,
    string Homepage,
    string Language,
    bool IsPrivate,
    DateTimeOffset? UpdatedUtc);

public sealed record GitHubRepositoryFetchResult(
    IReadOnlyList<GitHubRepositorySnapshot> Repositories,
    string Source);

internal static class GitHubRepositorySyncService
{
    private const int MaxPublicPages = 10;

    public static async Task<GitHubRepositoryFetchResult> FetchAsync(
        string owner,
        CancellationToken cancellationToken = default)
    {
        string normalizedOwner = string.IsNullOrWhiteSpace(owner) ? "julian-passebecq" : owner.Trim();

        IReadOnlyList<GitHubRepositorySnapshot>? fromGh = null;
        using (CancellationTokenSource ghTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            ghTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                fromGh = await TryFetchWithGitHubCliAsync(normalizedOwner, ghTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A stalled gh process should not block the public API fallback.
            }
        }

        if (fromGh is { Count: > 0 })
        {
            return new GitHubRepositoryFetchResult(fromGh, "GitHub CLI");
        }

        IReadOnlyList<GitHubRepositorySnapshot> publicRepos = await FetchPublicAsync(normalizedOwner, cancellationToken);
        return new GitHubRepositoryFetchResult(publicRepos, "GitHub public API");
    }

    private static async Task<IReadOnlyList<GitHubRepositorySnapshot>?> TryFetchWithGitHubCliAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo start = new()
            {
                FileName = "gh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            start.ArgumentList.Add("repo");
            start.ArgumentList.Add("list");
            start.ArgumentList.Add(owner);
            start.ArgumentList.Add("--limit");
            start.ArgumentList.Add("500");
            start.ArgumentList.Add("--json");
            start.ArgumentList.Add("name,nameWithOwner,url,description,isPrivate,updatedAt,homepageUrl,primaryLanguage");

            using Process process = new() { StartInfo = start };
            if (!process.Start())
            {
                return null;
            }

            try
            {
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                Task exitTask = process.WaitForExitAsync(cancellationToken);

                await Task.WhenAll(outputTask, errorTask, exitTask);
                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    _ = error;
                    return null;
                }

                return ParseGh(output);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process exited between the check and Kill.
                }
                catch (Win32Exception)
                {
                    // Cancellation still wins even if Windows refuses termination.
                }

                throw;
            }
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<GitHubRepositorySnapshot> ParseGh(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<GitHubRepositorySnapshot> result = [];

        foreach (JsonElement repo in document.RootElement.EnumerateArray())
        {
            string name = String(repo, "name");
            string fullName = String(repo, "nameWithOwner");
            string language = string.Empty;
            if (repo.TryGetProperty("primaryLanguage", out JsonElement primaryLanguage)
                && primaryLanguage.ValueKind == JsonValueKind.Object)
            {
                language = String(primaryLanguage, "name");
            }

            result.Add(new GitHubRepositorySnapshot(
                name,
                fullName,
                String(repo, "url"),
                String(repo, "description"),
                String(repo, "homepageUrl"),
                language,
                Bool(repo, "isPrivate"),
                Date(repo, "updatedAt")));
        }

        return Deduplicate(result);
    }

    private static async Task<IReadOnlyList<GitHubRepositorySnapshot>> FetchPublicAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JUtilityPalette", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        List<GitHubRepositorySnapshot> result = [];
        for (int page = 1; page <= MaxPublicPages; page++)
        {
            string url = $"https://api.github.com/users/{Uri.EscapeDataString(owner)}/repos?per_page=100&page={page}&sort=updated&direction=desc";
            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement.ArrayEnumerator rows = document.RootElement.EnumerateArray();
            int count = 0;
            foreach (JsonElement repo in rows)
            {
                count++;
                result.Add(new GitHubRepositorySnapshot(
                    String(repo, "name"),
                    String(repo, "full_name"),
                    String(repo, "html_url"),
                    String(repo, "description"),
                    String(repo, "homepage"),
                    String(repo, "language"),
                    Bool(repo, "private"),
                    Date(repo, "updated_at")));
            }

            if (count < 100)
            {
                break;
            }
        }

        return Deduplicate(result);
    }

    private static IReadOnlyList<GitHubRepositorySnapshot> Deduplicate(IEnumerable<GitHubRepositorySnapshot> repositories) =>
        repositories
            .Where(repo => !string.IsNullOrWhiteSpace(repo.Name) && !string.IsNullOrWhiteSpace(repo.Url))
            .GroupBy(repo => repo.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(repo => repo.UpdatedUtc).First())
            .OrderByDescending(repo => repo.UpdatedUtc)
            .ThenBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string String(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static DateTimeOffset? Date(JsonElement element, string property)
    {
        string value = String(element, property);
        return DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : null;
    }
}
