namespace JUtility.Core.Models;

public sealed record GitHubRepositorySnapshot(
    string Name,
    string FullName,
    string Url,
    string Description,
    string Homepage,
    string Language,
    bool IsPrivate,
    DateTimeOffset? UpdatedUtc);
