using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using JUtility.Core.Actions;

namespace JUtility.App;

// Optional local Claude Control server: "Claude Control" tab (a synthesized embedded web app), a start action,
// a status-bar health dot and live Launchpad data. Everything stays off until quick-actions.json has a
// "ClaudeControl" block. The only background work is one loopback GET /api/health every 30 s while the window is visible.
public partial class MainWindow
{
    private DispatcherTimer? _controlTimer;
    private SocketsHttpHandler? _controlHttp;
    private bool? _controlHealthy;
    private bool _controlChecking;

    private SocketsHttpHandler ControlHttp => _controlHttp ??= new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        ConnectTimeout = TimeSpan.FromSeconds(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>Called from <see cref="RegisterWebApps"/> after every settings change.</summary>
    private void RegisterClaudeControl()
    {
        ClaudeControlSettings? control = _quickActionSettings?.ClaudeControl;
        var actions = new List<(QuickActionDefinition, QuickActionHandler)>();
        if (!string.IsNullOrWhiteSpace(control?.StartCommand))
        {
            actions.Add((
                new QuickActionDefinition(ClaudeControl.StartActionId, "Start Claude Control", "Claude Control",
                    "Runs your Claude Control start command (set under Actions > Claude Control...).", "E768", null, true, ActionRisk.Safe),
                new QuickActionHandler(StartClaudeControl, ClaudeControlStartUnavailable)));
        }

        _quickActions.ReplaceDynamic(ClaudeControl.StartActionPrefix, actions);
        _controlHealthy = null;
        if (control is null)
        {
            _controlTimer?.Stop();
            ClaudeControlHealthButton.Visibility = Visibility.Collapsed;
            return;
        }

        ClaudeControlHealthButton.Visibility = Visibility.Visible;
        ShowControlHealth();
        if (_controlTimer is null)
        {
            _controlTimer = new DispatcherTimer { Interval = ClaudeControl.PollInterval };
            _controlTimer.Tick += (_, _) => { if (IsVisible && WindowState != WindowState.Minimized) _ = CheckControlHealthAsync(); };
            IsVisibleChanged += (_, _) => { if (IsVisible && _quickActionSettings?.ClaudeControl is not null) _ = CheckControlHealthAsync(); };
            Closed += (_, _) => { _controlTimer.Stop(); _controlHttp?.Dispose(); };
        }

        _controlTimer.Start();
        _ = CheckControlHealthAsync();
    }

    private string? ClaudeControlStartUnavailable()
    {
        string? command = _quickActionSettings?.ClaudeControl?.StartCommand?.Trim();
        if (string.IsNullOrEmpty(command)) return "No Claude Control start command is set (Actions > Claude Control...).";
        return File.Exists(command) ? null : $"The Claude Control start command was not found: {command}";
    }

    private void StartClaudeControl()
    {
        string command = _quickActionSettings!.ClaudeControl!.StartCommand!.Trim();
        Process.Start(new ProcessStartInfo(command)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(command)!,
            WindowStyle = ProcessWindowStyle.Minimized,
        });
        _viewModel.StatusText = "Starting Claude Control...";
        // The server needs a moment to bind its port; re-check a few times instead of waiting for the 30 s tick.
        foreach (int seconds in new[] { 2, 5, 10 })
        {
            var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            once.Tick += (_, _) => { once.Stop(); _ = CheckControlHealthAsync(); };
            once.Start();
        }
    }

    private async Task CheckControlHealthAsync()
    {
        ClaudeControlSettings? control = _quickActionSettings?.ClaudeControl;
        if (control is null || _controlChecking) return;
        _controlChecking = true;
        bool healthy;
        try { healthy = await ClaudeControl.IsHealthyAsync(control, ControlHttp); }
        finally { _controlChecking = false; }

        if (!ReferenceEquals(control, _quickActionSettings?.ClaudeControl)) return; // settings changed meanwhile
        bool? previous = _controlHealthy;
        _controlHealthy = healthy;
        ShowControlHealth();
        // Coming online (or going away) changes what the Launchpad card can show.
        if (previous is not null && previous != healthy && _launchpadBody?.IsVisible == true) RefreshLaunchpad();
    }

    private void ShowControlHealth()
    {
        (Brush brush, string state) = _controlHealthy switch
        {
            true => ((Brush)Brushes.SeaGreen, "online"),
            false => (Brushes.Firebrick, "offline"),
            null => (Brushes.Gray, "checking"),
        };
        ClaudeControlDot.Fill = brush;
        ClaudeControlHealthText.Text = "Claude Control " + state;
        string url = _quickActionSettings?.ClaudeControl?.Url ?? "";
        ClaudeControlHealthButton.ToolTip = _controlHealthy == false
            ? $"Claude Control is not answering at {url}. Click to start it."
            : $"Claude Control at {url} ({state}). Click to open the Claude Control tab.";
        AutomationProperties.SetName(ClaudeControlHealthButton, "Claude Control " + state);
        AutomationProperties.SetItemStatus(ClaudeControlHealthButton, state);
    }

    private void ClaudeControlHealth_Click(object sender, RoutedEventArgs e)
    {
        bool canStart = _quickActions.IsRegistered(ClaudeControl.StartActionId);
        RunQuickAction(_controlHealthy == false && canStart ? ClaudeControl.StartActionId : QuickWebApps.ActionId(ClaudeControl.WebAppId), ActionSurface.FullUi);
    }

    private void EditClaudeControl() => SessionAction(() =>
    {
        if (_quickActionSettings is null)
        {
            ShowOwnedMessage(ShelfUnavailable()!, ClaudeControl.Name, MessageBoxImage.Warning);
            return;
        }

        BringToFront();
        ClaudeControlSettings? current = _quickActionSettings.ClaudeControl;
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "Claude Control is your local dashboard server. When it is set here, Power Ops adds a \"Claude Control\" tab that embeds its page, "
                + "a Start action, a health dot in the status bar (one GET /api/health every 30 s while this window is visible) and live data on the "
                + "Launchpad card (GET /api/status). Power Ops never writes to it. Only addresses on this computer are accepted.",
            TextWrapping = TextWrapping.Wrap,
        });
        var enabled = new CheckBox { Content = "Use Claude Control", IsChecked = current is not null, Margin = new Thickness(0, 12, 0, 6) };
        var url = new TextBox { Text = current?.Url ?? "", MaxLength = QuickWebApps.MaxUrl };
        var start = new TextBox { Text = current?.StartCommand ?? "", MaxLength = 260 };
        AutomationProperties.SetName(enabled, "Use Claude Control");
        AutomationProperties.SetName(url, "Claude Control page address");
        AutomationProperties.SetName(start, "Claude Control start command");
        body.Children.Add(enabled);
        body.Children.Add(new TextBlock { Text = "Page address (for example http://127.0.0.1:<port>/home.html)" });
        body.Children.Add(url);
        body.Children.Add(new TextBlock { Text = "Start command (optional full path to a .cmd, .bat or .exe)", Margin = new Thickness(0, 8, 0, 0) });
        body.Children.Add(start);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        body.Children.Add(error);

        Window dialog = SessionDialogWindow(ClaudeControl.Name, body);
        dialog.Height = 400;
        body.Children.Add(SessionButton("Save", () =>
        {
            string? failure = TryUpdateQuickActionSettings(s => s.ClaudeControl = enabled.IsChecked == true
                ? new ClaudeControlSettings { Url = url.Text.Trim(), StartCommand = string.IsNullOrWhiteSpace(start.Text) ? null : start.Text.Trim() }
                : null);
            if (failure is null) dialog.DialogResult = true;
            else error.Text = failure;
        }));

        if (SessionDialog(dialog))
        {
            _viewModel.StatusText = _quickActionSettings.ClaudeControl is null ? "Claude Control turned off" : "Claude Control saved";
            RefreshLaunchpad();
        }
    });

    /// <summary>Live Launchpad data; falls back to the Effort Board files when the server is off.</summary>
    private async Task FillClaudeCardFromControlAsync(Panel content, ClaudeControlSettings control, string usagePath)
    {
        content.Children.Add(new TextBlock { Text = "Asking Claude Control...", Foreground = Brushes.DimGray });
        (ClaudeControlStatus? status, string? error) = await ClaudeControl.FetchStatusAsync(control, ControlHttp);
        content.Children.Clear();
        if (status is not null)
        {
            RenderControlStatus(content, status);
            return;
        }

        var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        note.Children.Add(new TextBlock
        {
            Text = error + " Showing the Effort Board files instead.", Foreground = Brushes.DarkOrange,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center,
        });
        if (_quickActions.IsRegistered(ClaudeControl.StartActionId))
            note.Children.Add(SessionButton("Start Claude Control", () => RunQuickAction(ClaudeControl.StartActionId, ActionSurface.FullUi)));
        content.Children.Add(note);
        if (File.Exists(usagePath)) RenderUsageFromFiles(content, usagePath);
        else content.Children.Add(new TextBlock { Text = "No usage.json either.", Foreground = Brushes.DimGray });
    }

    private void RenderControlStatus(Panel content, ClaudeControlStatus status)
    {
        DateTime now = DateTime.Now;
        content.Children.Add(new TextBlock
        {
            Text = $"Live from Claude Control · generated {ClaudeControl.Friendly(status.Generated, now)}"
                + (status.Plan is { Sampled.Length: > 0 } p ? $" · limits sampled {ClaudeControl.Friendly(p.Sampled, now)}" : ""),
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6),
        });
        if (status.Plan is { } plan)
        {
            content.Children.Add(UsageRow("5-hour limit", plan.FiveHour, ""));
            content.Children.Add(UsageRow("Week", plan.Week, ""));
        }

        if (status.Sessions is { } sessions)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Sessions: {sessions.Open} open · {sessions.Running} running · {sessions.NeedsYou} need you",
                Foreground = sessions.NeedsYou > 0 ? Brushes.DarkOrange : Brushes.Black,
                FontWeight = sessions.NeedsYou > 0 ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 8, 0, 0),
            });
        }

        var counts = new List<string>();
        if (status.Todo is int todo) counts.Add($"To-do {todo}");
        if (status.OpenPrs is int prs) counts.Add($"open PRs {prs}");
        if (status.ChecksToAct is int checks) counts.Add($"checks to act {checks}");
        if (counts.Count > 0) content.Children.Add(new TextBlock { Text = string.Join(" · ", counts) });
        if (status.AuditName.Length > 0) content.Children.Add(new TextBlock { Text = $"Latest audit: {status.AuditName} {status.AuditStatus}".TrimEnd() });

        if (status.Urgent.Count == 0) return;
        content.Children.Add(new TextBlock { Text = "Needs attention", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        foreach (ClaudeControlUrgent item in status.Urgent.Take(5))
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            string prefix = $"{ClaudeControl.Friendly(item.Time, now)} · {(item.Project.Length > 0 ? item.Project + " · " : "")}";
            line.Inlines.Add(new Run(prefix) { Foreground = Brushes.DimGray });
            if (item.Link is Uri link)
            {
                var hyperlink = new Hyperlink(new Run(item.Text)) { ToolTip = link.Scheme == "claude" ? "Open in the Claude app" : link.AbsoluteUri };
                hyperlink.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _viewModel.StatusText = "Could not open the link: " + ex.Message; }
                };
                line.Inlines.Add(hyperlink);
            }
            else
            {
                line.Inlines.Add(new Run(item.Text));
            }

            content.Children.Add(line);
        }
    }
}
