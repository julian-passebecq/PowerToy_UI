using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JUtility.Core.Services;

public enum ClaudeSessionStatus
{
    NeedsYou = 0,
    Working = 1,
    Idle = 2,
    Done = 3,
}

/// <summary>What the tail of a transcript says about the latest turn.</summary>
public sealed class TranscriptTail
{
    public string? LastAssistantText { get; set; }
    public string? PendingToolName { get; set; }
    public bool LastIsUserPrompt { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? Cwd { get; set; }
    public string? GitBranch { get; set; }
    public int? PrNumber { get; set; }
    public string? PrUrl { get; set; }
}

public sealed record ClaudeSessionPr(int Number, string Url, string State);

/// <summary>Session metadata kept by the Claude desktop app (claude-code-sessions/**/local_*.json).</summary>
public sealed record ClaudeDesktopSession(
    string LocalId,
    string CliSessionId,
    string? Title,
    string? Branch,
    string? OriginCwd,
    bool IsArchived,
    IReadOnlyList<ClaudeSessionPr> Prs);

public sealed record ClaudeSessionInfo(
    string SessionId,
    string Project,
    string ProjectType,
    string EffortCeiling,
    string Title,
    string? Branch,
    ClaudeSessionPr? Pr,
    ClaudeSessionStatus Status,
    string StatusReason,
    DateTimeOffset LastActivity,
    string LastAssistantText,
    int SubagentCount,
    bool IsArchived,
    string? DeepLink);

public static class TranscriptTailParser
{
    /// <summary>Parses JSONL lines (oldest first). Partial or malformed lines are skipped.</summary>
    public static TranscriptTail Parse(IEnumerable<string> lines)
    {
        TranscriptTail tail = new();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                string type = GetString(root, "type") ?? "";
                if (type == "pr-link")
                {
                    if (root.TryGetProperty("prNumber", out JsonElement n) && n.TryGetInt32(out int number))
                    {
                        tail.PrNumber = number;
                    }
                    tail.PrUrl = GetString(root, "prUrl") ?? tail.PrUrl;
                    continue;
                }

                if (type is not ("user" or "assistant"))
                {
                    continue;
                }

                if (root.TryGetProperty("isSidechain", out JsonElement side) && side.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                tail.Cwd = GetString(root, "cwd") ?? tail.Cwd;
                tail.GitBranch = GetString(root, "gitBranch") ?? tail.GitBranch;
                if (DateTimeOffset.TryParse(GetString(root, "timestamp"), out DateTimeOffset at))
                {
                    tail.LastMessageAt = at;
                }

                if (!root.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object
                    || !message.TryGetProperty("content", out JsonElement content))
                {
                    continue;
                }

                if (type == "assistant")
                {
                    ApplyAssistant(tail, content);
                }
                else
                {
                    bool isMeta = root.TryGetProperty("isMeta", out JsonElement meta) && meta.ValueKind == JsonValueKind.True;
                    ApplyUser(tail, content, isMeta);
                }
            }
        }

        return tail;
    }

    private static void ApplyAssistant(TranscriptTail tail, JsonElement content)
    {
        if (tail.LastIsUserPrompt)
        {
            // New turn: drop text from the previous one.
            tail.LastAssistantText = null;
            tail.LastIsUserPrompt = false;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            SetText(tail, content.GetString());
            tail.PendingToolName = null;
            return;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement block in content.EnumerateArray())
        {
            switch (GetString(block, "type"))
            {
                case "text":
                    SetText(tail, GetString(block, "text"));
                    tail.PendingToolName = null;
                    break;
                case "tool_use":
                    tail.PendingToolName = GetString(block, "name") ?? "tool";
                    break;
            }
        }
    }

    private static void ApplyUser(TranscriptTail tail, JsonElement content, bool isMeta)
    {
        bool hasToolResult = false;
        bool hasText = content.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(content.GetString());
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement block in content.EnumerateArray())
            {
                string? blockType = GetString(block, "type");
                hasToolResult |= blockType == "tool_result";
                hasText |= blockType is "text" or "image";
            }
        }

        if (hasToolResult)
        {
            tail.PendingToolName = null;
        }
        else if (hasText && !isMeta)
        {
            tail.PendingToolName = null;
            tail.LastIsUserPrompt = true;
        }
    }

    private static void SetText(TranscriptTail tail, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            tail.LastAssistantText = text.Trim();
        }
    }

    internal static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

public static class ClaudeSessionClassifier
{
    public static readonly TimeSpan WorkingWindow = TimeSpan.FromSeconds(60);

    /// <summary>A plain tool call (build, tests) may run silently for minutes before we suspect a permission prompt.</summary>
    public static readonly TimeSpan ToolRunWindow = TimeSpan.FromMinutes(5);

    private static readonly Regex WaitingPattern = new(
        @"\b(waiting (for|on) (you|your|merge|approval|manual)|awaiting (your|approval|merge)|please (test|confirm|approve|check)|manual test|let me know|should i|do you want|want me to|shall i|your call|which (one|option) do you)\b|j'attends|veux-tu|voulez-vous|dis-moi|à toi de|je te laisse tester",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DonePattern = new(
        @"\b(merged|done|all set|completed?|finished|shipped)\b|fusionn|termin[ée]|c'est fait",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static (ClaudeSessionStatus Status, string Reason) Classify(
        TranscriptTail tail,
        DateTimeOffset lastWrite,
        DateTimeOffset now,
        bool prMerged)
    {
        if (tail.PendingToolName is "AskUserQuestion" or "ExitPlanMode")
        {
            return (ClaudeSessionStatus.NeedsYou, tail.PendingToolName == "AskUserQuestion" ? "Question waiting for your answer" : "Plan waiting for approval");
        }

        if (now - lastWrite < WorkingWindow)
        {
            return (ClaudeSessionStatus.Working, "Active in the last minute");
        }

        if (tail.PendingToolName is { } running && now - lastWrite < ToolRunWindow)
        {
            return (ClaudeSessionStatus.Working, $"Running {running}");
        }

        if (tail.PendingToolName is { } tool)
        {
            return (ClaudeSessionStatus.NeedsYou, $"Stopped on {tool}: permission prompt or interrupted");
        }

        if (tail.LastIsUserPrompt)
        {
            return (ClaudeSessionStatus.Idle, "Your last message has no reply (interrupted?)");
        }

        string text = tail.LastAssistantText ?? "";
        if (text.Length > 0)
        {
            string ending = LastLines(text, 3);
            if (EndsWithQuestion(text))
            {
                return (ClaudeSessionStatus.NeedsYou, "Asked you a question");
            }

            if (WaitingPattern.IsMatch(ending))
            {
                return (ClaudeSessionStatus.NeedsYou, "Waiting for your input");
            }

            if (DonePattern.IsMatch(text))
            {
                return (ClaudeSessionStatus.Done, "Reported done");
            }
        }

        if (prMerged)
        {
            return (ClaudeSessionStatus.Done, "PR merged");
        }

        return (ClaudeSessionStatus.Idle, "Idle");
    }

    private static bool EndsWithQuestion(string text)
    {
        string last = LastLines(text, 1).TrimEnd('*', '_', '`', ' ', ')', '"', '»');
        return last.EndsWith('?');
    }

    private static string LastLines(string text, int count)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - count)));
    }
}

/// <summary>Project tiers from the user's global CLAUDE.md effort table.</summary>
public sealed class ClaudeProjectTiers
{
    private readonly Dictionary<string, string> _ceilings = new(StringComparer.OrdinalIgnoreCase);
    private string _defaultCeiling = "high";

    public static ClaudeProjectTiers Parse(string? markdown)
    {
        ClaudeProjectTiers tiers = new();
        if (string.IsNullOrEmpty(markdown))
        {
            return tiers;
        }

        foreach (string raw in markdown.Split('\n'))
        {
            string[] cells = raw.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 2 || cells[1] is not ("low" or "medium" or "high" or "xhigh" or "max"))
            {
                continue;
            }

            if (cells[0].StartsWith("everything else", StringComparison.OrdinalIgnoreCase))
            {
                tiers._defaultCeiling = cells[1];
            }
            else if (cells[0].Contains('\\') || cells[0].Contains('/'))
            {
                tiers._ceilings[ProjectNameFromPath(cells[0])] = cells[1];
            }
        }

        return tiers;
    }

    public string CeilingFor(string project) => _ceilings.TryGetValue(project, out string? c) ? c : _defaultCeiling;

    public static string ProjectNameFromPath(string path)
    {
        string normalized = path.Replace('/', '\\').TrimEnd('\\');
        int worktree = normalized.IndexOf("\\.claude\\worktrees\\", StringComparison.OrdinalIgnoreCase);
        if (worktree >= 0)
        {
            normalized = normalized[..worktree];
        }

        int slash = normalized.LastIndexOf('\\');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }
}

/// <summary>Read-only scanner for Claude Code sessions on this PC. Caches per-file results by size+mtime.</summary>
public sealed class ClaudeSessionMonitor
{
    public const int TailBytes = 96 * 1024;
    public static readonly TimeSpan OldAfter = TimeSpan.FromDays(3);

    private readonly string _projectsRoot;
    private readonly IReadOnlyList<string> _desktopSessionsRoots;
    private readonly string _globalClaudeMd;
    private readonly Dictionary<string, (long Length, DateTime Mtime, TranscriptTail Tail)> _tailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Mtime, ClaudeDesktopSession? Session)> _desktopCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _projectTypeCache = new(StringComparer.OrdinalIgnoreCase);

    public ClaudeSessionMonitor(string projectsRoot, IReadOnlyList<string> desktopSessionsRoots, string globalClaudeMd)
    {
        _projectsRoot = projectsRoot;
        _desktopSessionsRoots = desktopSessionsRoots;
        _globalClaudeMd = globalClaudeMd;
    }

    public static ClaudeSessionMonitor CreateDefault()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        List<string> desktopRoots = [Path.Combine(appData, "Claude", "claude-code-sessions")];

        // The MSIX-packaged Claude app writes its Roaming data into a virtualized package folder,
        // which unpackaged processes only see under %LOCALAPPDATA%\Packages.
        desktopRoots.AddRange(SafeEnumerate(() => Directory.EnumerateDirectories(Path.Combine(localAppData, "Packages"), "Claude_*"))
            .Select(package => Path.Combine(package, "LocalCache", "Roaming", "Claude", "claude-code-sessions")));

        return new ClaudeSessionMonitor(
            Path.Combine(home, ".claude", "projects"),
            desktopRoots,
            Path.Combine(home, ".claude", "CLAUDE.md"));
    }

    public string ProjectsRoot => _projectsRoot;

    public IReadOnlyList<ClaudeSessionInfo> Scan(DateTimeOffset now)
    {
        Dictionary<string, ClaudeDesktopSession> desktop = LoadDesktopSessions();
        ClaudeProjectTiers tiers = ClaudeProjectTiers.Parse(SafeReadAll(_globalClaudeMd));
        List<ClaudeSessionInfo> sessions = [];
        if (!Directory.Exists(_projectsRoot))
        {
            return sessions;
        }

        foreach (string projectDir in SafeEnumerate(() => Directory.EnumerateDirectories(_projectsRoot)))
        {
            foreach (string file in SafeEnumerate(() => Directory.EnumerateFiles(projectDir, "*.jsonl")))
            {
                ClaudeSessionInfo? info = BuildSession(file, projectDir, desktop, tiers, now);
                if (info is not null)
                {
                    sessions.Add(info);
                }
            }
        }

        return Sort(sessions);
    }

    public static List<ClaudeSessionInfo> Sort(IEnumerable<ClaudeSessionInfo> sessions) =>
        sessions.OrderBy(s => s.Status).ThenByDescending(s => s.LastActivity).ToList();

    public static bool IsHiddenByDefault(ClaudeSessionInfo session, DateTimeOffset now) =>
        session.IsArchived || (session.Status != ClaudeSessionStatus.NeedsYou && now - session.LastActivity > OldAfter);

    public static string ProjectFromFolderName(string folder)
    {
        string name = folder;
        int worktree = name.IndexOf("--claude-worktrees-", StringComparison.OrdinalIgnoreCase);
        if (worktree >= 0)
        {
            name = name[..worktree];
        }

        Match drive = Regex.Match(name, "^[A-Za-z]--(PROJ-)?");
        return drive.Success ? name[drive.Length..] : name;
    }

    private ClaudeSessionInfo? BuildSession(string file, string projectDir, Dictionary<string, ClaudeDesktopSession> desktop, ClaudeProjectTiers tiers, DateTimeOffset now)
    {
        FileInfo fi = new(file);
        if (!fi.Exists || fi.Length == 0)
        {
            return null;
        }

        string id = Path.GetFileNameWithoutExtension(file);
        TranscriptTail tail = ReadTail(fi);
        desktop.TryGetValue(id, out ClaudeDesktopSession? meta);
        if (tail.LastMessageAt is null && meta is null)
        {
            return null; // Not a conversation (e.g. metadata-only file).
        }

        string? projectPath = meta?.OriginCwd ?? tail.Cwd;
        string project = projectPath is not null
            ? ClaudeProjectTiers.ProjectNameFromPath(projectPath)
            : ProjectFromFolderName(Path.GetFileName(projectDir));
        if (project.StartsWith("scratch", StringComparison.OrdinalIgnoreCase) || projectPath?.Contains("scratch-workspaces", StringComparison.OrdinalIgnoreCase) == true)
        {
            project = "Scratch";
        }

        ClaudeSessionPr? pr = meta?.Prs.LastOrDefault();
        if (pr is null && tail.PrNumber is int prNumber)
        {
            pr = new ClaudeSessionPr(prNumber, tail.PrUrl ?? "", "");
        }

        bool prMerged = meta is not null && meta.Prs.Count > 0 && meta.Prs.All(p => string.Equals(p.State, "MERGED", StringComparison.OrdinalIgnoreCase));
        DateTimeOffset lastWrite = new(fi.LastWriteTimeUtc, TimeSpan.Zero);
        (ClaudeSessionStatus status, string reason) = ClaudeSessionClassifier.Classify(tail, lastWrite, now, prMerged);

        string title = !string.IsNullOrWhiteSpace(meta?.Title)
            ? meta!.Title!
            : FirstLine(tail.LastAssistantText) is { Length: > 0 } fallback ? Truncate(fallback, 60) : "Session " + id[..Math.Min(8, id.Length)];

        return new ClaudeSessionInfo(
            id,
            project,
            ProjectTypeFor(projectPath),
            tiers.CeilingFor(project),
            title,
            meta?.Branch ?? tail.GitBranch,
            pr,
            status,
            reason,
            lastWrite,
            tail.LastAssistantText ?? "",
            CountSubagents(Path.Combine(projectDir, id)),
            meta?.IsArchived ?? false,
            meta is null ? null : "claude://claude.ai/epitaxy/" + meta.LocalId);
    }

    private TranscriptTail ReadTail(FileInfo fi)
    {
        if (_tailCache.TryGetValue(fi.FullName, out var cached) && cached.Length == fi.Length && cached.Mtime == fi.LastWriteTimeUtc)
        {
            return cached.Tail;
        }

        TranscriptTail tail = ReadTailLines(fi.FullName, fi.Length, TailBytes);
        if (tail.LastMessageAt is null && fi.Length > TailBytes)
        {
            // Huge tool results can push the last message out of the window; widen once.
            tail = ReadTailLines(fi.FullName, fi.Length, TailBytes * 8);
        }

        _tailCache[fi.FullName] = (fi.Length, fi.LastWriteTimeUtc, tail);
        return tail;
    }

    private static TranscriptTail ReadTailLines(string path, long length, int bytes)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, length - bytes);
            stream.Seek(start, SeekOrigin.Begin);
            byte[] buffer = new byte[length - start];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }

            string text = Encoding.UTF8.GetString(buffer, 0, read);
            IEnumerable<string> lines = text.Split('\n');
            if (start > 0)
            {
                lines = lines.Skip(1); // First line is likely partial.
            }

            return TranscriptTailParser.Parse(lines);
        }
        catch (IOException)
        {
            return new TranscriptTail();
        }
        catch (UnauthorizedAccessException)
        {
            return new TranscriptTail();
        }
    }

    private Dictionary<string, ClaudeDesktopSession> LoadDesktopSessions()
    {
        Dictionary<string, ClaudeDesktopSession> result = new(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files = _desktopSessionsRoots
            .Where(Directory.Exists)
            .SelectMany(root => SafeEnumerate(() => Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories)));
        foreach (string file in files)
        {
            DateTime mtime;
            try
            {
                mtime = File.GetLastWriteTimeUtc(file);
            }
            catch (IOException)
            {
                continue;
            }

            if (!_desktopCache.TryGetValue(file, out var cached) || cached.Mtime != mtime)
            {
                cached = (mtime, ParseDesktopSession(SafeReadAll(file)));
                _desktopCache[file] = cached;
            }

            if (cached.Session is { } session)
            {
                result[session.CliSessionId] = session;
            }
        }

        return result;
    }

    public static ClaudeDesktopSession? ParseDesktopSession(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            // Desktop files embed deeply nested MCP tool schemas.
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1024 });
            JsonElement root = doc.RootElement;
            string? localId = TranscriptTailParser.GetString(root, "sessionId");
            string? cliId = TranscriptTailParser.GetString(root, "cliSessionId");
            if (localId is null || cliId is null)
            {
                return null;
            }

            List<ClaudeSessionPr> prs = [];
            if (root.TryGetProperty("prs", out JsonElement prArray) && prArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement pr in prArray.EnumerateArray())
                {
                    if (pr.TryGetProperty("prNumber", out JsonElement n) && n.TryGetInt32(out int number))
                    {
                        prs.Add(new ClaudeSessionPr(number, TranscriptTailParser.GetString(pr, "url") ?? "", TranscriptTailParser.GetString(pr, "state") ?? ""));
                    }
                }
            }

            bool archived = root.TryGetProperty("isArchived", out JsonElement a) && a.ValueKind == JsonValueKind.True;
            return new ClaudeDesktopSession(
                localId,
                cliId,
                TranscriptTailParser.GetString(root, "title"),
                TranscriptTailParser.GetString(root, "branch"),
                TranscriptTailParser.GetString(root, "originCwd") ?? TranscriptTailParser.GetString(root, "cwd"),
                archived,
                prs);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string ProjectTypeFor(string? projectPath)
    {
        if (projectPath is null)
        {
            return "dev";
        }

        string root = projectPath;
        int worktree = root.IndexOf("\\.claude\\worktrees\\", StringComparison.OrdinalIgnoreCase);
        if (worktree >= 0)
        {
            root = root[..worktree];
        }

        if (_projectTypeCache.TryGetValue(root, out string? cached))
        {
            return cached;
        }

        string type = "dev";
        string? md = SafeReadAll(Path.Combine(root, "CLAUDE.md"));
        Match m = md is null ? Match.Empty : Regex.Match(md, @"Project type:\s*\**\s*(work|dev|test|sandbox)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            type = m.Groups[1].Value.ToLowerInvariant() == "sandbox" ? "test" : m.Groups[1].Value.ToLowerInvariant();
        }

        _projectTypeCache[root] = type;
        return type;
    }

    private static int CountSubagents(string sessionDir)
    {
        string dir = Path.Combine(sessionDir, "subagents");
        return Directory.Exists(dir) ? SafeEnumerate(() => Directory.EnumerateFiles(dir, "*.jsonl")).Count() : 0;
    }

    private static string? SafeReadAll(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> source)
    {
        try
        {
            return source().ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string FirstLine(string? text) =>
        text?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
