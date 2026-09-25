using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JUtility.Core.Services;

namespace JUtility.App;

public sealed class SessionGroupView
{
    public required string Name { get; init; }
    public required string Meta { get; init; }
    public required int NeedsYou { get; init; }
    public required IReadOnlyList<SessionRowView> Sessions { get; init; }
    public bool IsExpanded { get; set; }
    public string NeedsYouLabel => "! " + NeedsYou.ToString(CultureInfo.InvariantCulture);
    public Visibility NeedsYouVisibility => NeedsYou > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class SessionRowView
{
    public SessionRowView(ClaudeSessionInfo info, DateTimeOffset now)
    {
        Info = info;
        Ago = FormatAgo(now - info.LastActivity);
        LastActivityText = info.LastActivity.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public ClaudeSessionInfo Info { get; }
    public string Title => Info.Title;
    public string Ago { get; }
    public string LastActivityText { get; }
    public string FullText => string.IsNullOrWhiteSpace(Info.LastAssistantText) ? "(no assistant message yet)" : Info.LastAssistantText;
    public string LastLine => string.Join(" ", FullText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public string Meta
    {
        get
        {
            List<string> parts = [];
            if (!string.IsNullOrWhiteSpace(Info.Branch)) parts.Add(Info.Branch!);
            if (Info.Pr is { } pr) parts.Add($"PR #{pr.Number}" + (string.IsNullOrEmpty(pr.State) ? "" : " · " + pr.State.ToLowerInvariant()));
            if (Info.SubagentCount > 0) parts.Add($"{Info.SubagentCount} subagent{(Info.SubagentCount == 1 ? "" : "s")}");
            if (Info.IsArchived) parts.Add("archived");
            return parts.Count == 0 ? Info.StatusReason : string.Join("  ·  ", parts);
        }
    }

    public string Symbol => Info.Status switch
    {
        ClaudeSessionStatus.NeedsYou => "!",
        ClaudeSessionStatus.Done => "✓",
        ClaudeSessionStatus.Idle => "–",
        _ => "",
    };

    // Same palette as the Effort Board (teal = OK, amber = attention), resolved from the active
    // light/dark theme. Rows are rebuilt on every render, including after a theme switch.
    public Brush StatusBrush => ThemeBrush(Info.Status switch
    {
        ClaudeSessionStatus.NeedsYou => "StatusAttentionBrush",
        ClaudeSessionStatus.Idle => "StatusIdleBrush",
        _ => "StatusOkBrush",
    });

    public Brush SymbolForeground => Info.Status == ClaudeSessionStatus.NeedsYou ? ThemeBrush("OnStatusBrush") : StatusBrush;
    public double SymbolBackgroundOpacity => Info.Status switch
    {
        ClaudeSessionStatus.NeedsYou => 1.0,
        ClaudeSessionStatus.Working => 0.0,
        _ => 0.14,
    };

    public Brush RowBackground => Info.Status == ClaudeSessionStatus.NeedsYou ? ThemeBrush("AttentionRowBrush") : Brushes.Transparent;
    public Visibility WorkingVisibility => Info.Status == ClaudeSessionStatus.Working ? Visibility.Visible : Visibility.Collapsed;
    public string StatusTooltip => $"{MainWindow.SessionStatusLabel(Info.Status)} — {Info.StatusReason}";
    public string GoTooltip => Info.DeepLink ?? "No deep link found: opens Claude and copies the session title";

    private static string FormatAgo(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h";
        return $"{(int)span.TotalDays}d";
    }

    private static Brush ThemeBrush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
}

public partial class MainWindow
{
    private static readonly string[] SessionStatusKeys = ["all", "needs", "working", "idle", "done"];

    private readonly ClaudeSessionMonitor _sessionMonitor = ClaudeSessionMonitor.CreateDefault();
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _sessionDebounce = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, bool> _sessionGroupExpanded = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _sessionWatcher;
    private IReadOnlyList<ClaudeSessionInfo> _sessions = [];
    private string _sessionTypeFilter = "all";
    private bool _sessionScanRunning;
    private int _lastNeedsYouCount = -1;

    internal static string SessionStatusLabel(ClaudeSessionStatus status) => status switch
    {
        ClaudeSessionStatus.NeedsYou => "Needs you",
        ClaudeSessionStatus.Working => "Working",
        ClaudeSessionStatus.Done => "Done",
        _ => "Idle",
    };

    private static ClaudeSessionStatus? StatusForKey(string key) => key switch
    {
        "needs" => ClaudeSessionStatus.NeedsYou,
        "working" => ClaudeSessionStatus.Working,
        "idle" => ClaudeSessionStatus.Idle,
        "done" => ClaudeSessionStatus.Done,
        _ => null,
    };

    private void InitializeSessionsMonitor()
    {
        _sessionTimer.Tick += (_, _) => RefreshSessionsNow();
        _sessionDebounce.Tick += (_, _) =>
        {
            _sessionDebounce.Stop();
            RefreshSessionsNow();
        };

        try
        {
            if (Directory.Exists(_sessionMonitor.ProjectsRoot))
            {
                _sessionWatcher = new FileSystemWatcher(_sessionMonitor.ProjectsRoot, "*.jsonl")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                _sessionWatcher.Changed += SessionFileChanged;
                _sessionWatcher.Created += SessionFileChanged;
                _sessionWatcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _sessionWatcher = null; // The 10 s timer still keeps the view fresh.
        }

        if (App.Theme is { } theme)
        {
            theme.ThemeChanged += SessionsTheme_Changed;
        }

        Loaded += (_, _) =>
        {
            _sessionTimer.Start();
            RefreshSessionsNow();
        };
        Closed += (_, _) =>
        {
            _sessionTimer.Stop();
            _sessionWatcher?.Dispose();
            if (App.Theme is { } closingTheme)
            {
                closingTheme.ThemeChanged -= SessionsTheme_Changed;
            }
        };
    }

    private void SessionsTheme_Changed(object? sender, EventArgs e)
    {
        // Row status brushes are resolved when rows are built, so rebuild them for the new palette.
        if (_activeModule == "Sessions")
        {
            RenderSessions();
        }
    }

    private void SessionFileChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + "subagents" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            // Only speed up updates when the transcript is quiet otherwise the 10 s tick is enough.
            if (!_sessionDebounce.IsEnabled)
            {
                _sessionDebounce.Start();
            }
        });
    }

    private async void RefreshSessionsNow()
    {
        if (_sessionScanRunning)
        {
            return;
        }

        _sessionScanRunning = true;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            _ = RefreshAuditCardAsync();
            _sessions = await Task.Run(() => _sessionMonitor.Scan(now));
            UpdateSessionsBadge();
            if (_activeModule == "Sessions")
            {
                RenderSessions();
                RefreshSecondaryNavigation();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SessionsSummaryText.Text = "Could not scan sessions: " + ex.Message;
        }
        finally
        {
            _sessionScanRunning = false;
        }
    }

    private IEnumerable<ClaudeSessionInfo> VisibleSessionsBeforeStatusFilter(DateTimeOffset now)
    {
        bool showOld = SessionsShowOldCheck.IsChecked == true;
        return _sessions.Where(s =>
            (showOld || !ClaudeSessionMonitor.IsHiddenByDefault(s, now))
            && (_sessionTypeFilter == "all" || string.Equals(s.ProjectType, _sessionTypeFilter, StringComparison.OrdinalIgnoreCase)));
    }

    private void AddSessionStatusNav(Action<string, string, int> add)
    {
        List<ClaudeSessionInfo> visible = VisibleSessionsBeforeStatusFilter(DateTimeOffset.UtcNow).ToList();
        add("all", "All sessions", visible.Count);
        foreach (string key in SessionStatusKeys.Skip(1))
        {
            ClaudeSessionStatus status = StatusForKey(key)!.Value;
            add(key, SessionStatusLabel(status), visible.Count(s => s.Status == status));
        }
    }

    private void RenderSessions()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<ClaudeSessionInfo> visible = VisibleSessionsBeforeStatusFilter(now).ToList();
        string statusKey = GetModuleFilter("Sessions");
        ClaudeSessionStatus? statusFilter = StatusForKey(statusKey);
        List<ClaudeSessionInfo> shown = statusFilter is null ? visible : visible.Where(s => s.Status == statusFilter).ToList();

        RenderSessionChips(visible, statusKey);

        List<SessionGroupView> groups = shown
            .GroupBy(s => s.Project, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                List<ClaudeSessionInfo> ordered = ClaudeSessionMonitor.Sort(g);
                ClaudeSessionInfo first = ordered[0];
                int needs = ordered.Count(s => s.Status == ClaudeSessionStatus.NeedsYou);
                return new SessionGroupView
                {
                    Name = g.Key,
                    Meta = $"{first.ProjectType} · ceiling {first.EffortCeiling} · {ordered.Count} session{(ordered.Count == 1 ? "" : "s")}",
                    NeedsYou = needs,
                    Sessions = ordered.Select(s => new SessionRowView(s, now)).ToList(),
                    IsExpanded = _sessionGroupExpanded.TryGetValue(g.Key, out bool expanded) ? expanded : true,
                };
            })
            .OrderBy(g => g.Sessions.Min(r => r.Info.Status))
            .ThenByDescending(g => g.Sessions.Max(r => r.Info.LastActivity))
            .ToList();

        SessionGroupList.ItemsSource = groups;

        int hidden = _sessions.Count - visible.Count;
        int needsYou = visible.Count(s => s.Status == ClaudeSessionStatus.NeedsYou);
        int working = visible.Count(s => s.Status == ClaudeSessionStatus.Working);
        SessionsSummaryText.Text =
            $"{needsYou} need you · {working} working · {visible.Count} shown" +
            (hidden > 0 ? $" · {hidden} archived/old hidden" : "") +
            $" · updated {DateTime.Now:HH:mm:ss}";

        SessionsEmptyText.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionsEmptyText.Text = _sessions.Count == 0
            ? $"No Claude Code transcripts found under {_sessionMonitor.ProjectsRoot}."
            : "No session matches these filters.";
    }

    private void RenderSessionChips(List<ClaudeSessionInfo> visible, string statusKey)
    {
        RebuildChips(SessionsStatusChips, SessionStatusKeys.Select(key =>
        {
            ClaudeSessionStatus? status = StatusForKey(key);
            int count = status is null ? visible.Count : visible.Count(s => s.Status == status);
            string label = status switch
            {
                ClaudeSessionStatus.NeedsYou => "! Needs you",
                ClaudeSessionStatus.Working => "● Working",
                ClaudeSessionStatus.Done => "✓ Done",
                ClaudeSessionStatus.Idle => "– Idle",
                _ => "All",
            };
            return (key, $"{label} ({count})", key == statusKey);
        }), SessionStatusChip_Click);

        IEnumerable<string> types = new[] { "all" }.Concat(_sessions.Select(s => s.ProjectType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t));
        RebuildChips(SessionsTypeChips, types.Select(t => (t, t == "all" ? "All" : t, string.Equals(t, _sessionTypeFilter, StringComparison.OrdinalIgnoreCase))), SessionTypeChip_Click);
    }

    private static void RebuildChips(WrapPanel panel, IEnumerable<(string Key, string Label, bool Active)> chips, RoutedEventHandler handler)
    {
        while (panel.Children.Count > 1)
        {
            panel.Children.RemoveAt(1); // Keep the leading caption.
        }

        foreach ((string key, string label, bool active) in chips)
        {
            ToggleButton chip = new()
            {
                Content = label,
                Tag = key,
                IsChecked = active,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 4),
            };
            chip.Click += handler;
            panel.Children.Add(chip);
        }
    }

    private void SessionStatusChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string key)
        {
            _moduleFilters["Sessions"] = key;
            RefreshSecondaryNavigation();
            RenderSessions();
        }
    }

    private void SessionTypeChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string key)
        {
            _sessionTypeFilter = key;
            RefreshSecondaryNavigation();
            RenderSessions();
        }
    }

    private void SessionsFilter_Click(object sender, RoutedEventArgs e)
    {
        RefreshSecondaryNavigation();
        RenderSessions();
    }

    private void SessionsRefresh_Click(object sender, RoutedEventArgs e) => RefreshSessionsNow();

    private void SessionGroup_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { Tag: string name } expander)
        {
            _sessionGroupExpanded[name] = expander.IsExpanded;
        }
    }

    private void SessionGo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SessionRowView row)
        {
            return;
        }

        string target = row.Info.DeepLink ?? "claude://";
        if (row.Info.DeepLink is null)
        {
            try
            {
                Clipboard.SetText(row.Info.Title);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Clipboard busy: opening the app still helps.
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            _viewModel.StatusText = row.Info.DeepLink is null
                ? $"Opened Claude — title \"{row.Info.Title}\" copied to find the session."
                : $"Opened session \"{row.Info.Title}\" in Claude.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _viewModel.StatusText = "Could not open Claude: " + ex.Message;
        }
    }

    private void UpdateSessionsBadge()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int needsYou = _sessions.Count(s => s.Status == ClaudeSessionStatus.NeedsYou && !ClaudeSessionMonitor.IsHiddenByDefault(s, now));
        SessionsNeedsYouBadge.Visibility = needsYou > 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionsNeedsYouBadgeText.Text = needsYou.ToString(CultureInfo.InvariantCulture);
        if (needsYou == _lastNeedsYouCount)
        {
            return;
        }

        _lastNeedsYouCount = needsYou;
        ShellTaskbarInfo.Overlay = needsYou > 0 ? CreateBadgeImage(needsYou) : null;
        ShellTaskbarInfo.Description = needsYou > 0 ? $"{needsYou} Claude session(s) need you" : string.Empty;
    }

    private static ImageSource CreateBadgeImage(int count)
    {
        const int size = 32;
        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xB2, 0x5D, 0x12)), new Pen(Brushes.White, 2), new Point(size / 2.0, size / 2.0), size / 2.0 - 1, size / 2.0 - 1);
            FormattedText text = new(
                count > 9 ? "9+" : count.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                count > 9 ? 15 : 19,
                Brushes.White,
                1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
