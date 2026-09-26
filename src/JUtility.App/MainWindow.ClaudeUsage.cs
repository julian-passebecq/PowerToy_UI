using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;
using JUtility.Core.Reports;

namespace JUtility.App;

// Read-only "Claude usage" card on the Launchpad: live GET /api/status from Claude Control when it is configured
// (MainWindow.ClaudeControl.cs), otherwise, or when that server is off, the Effort Board's local usage.json.
// Data is read only when the Launchpad renders or Refresh is pressed; Power Ops never writes to either source.
public partial class MainWindow
{
    private void RenderClaudeUsageCard(Panel host)
    {
        string path = ClaudeUsage.DefaultPath();
        ClaudeControlSettings? control = _quickActionSettings?.ClaudeControl;
        if (control is null && !File.Exists(path)) return; // Optional: no card at all for users without the Effort Board or Claude Control.

        var body = new StackPanel { Margin = new Thickness(12) };
        var header = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Children.Add(SessionButton("Refresh", RefreshLaunchpad));
        // Companion pages are the user's own web apps (URLs live in quick-actions.json, never in this public repo).
        // A button appears only when a web app with that name is configured.
        foreach (string name in new[] { "Claude Home", "Claude Today" })
        {
            WebAppEntry? app = _quickActionSettings?.WebApps.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (app is not null) buttons.Children.Add(SessionButton("Open " + name, () => RunQuickAction(QuickWebApps.ActionId(app.Id), ActionSurface.FullUi)));
        }
        if (control is not null)
            buttons.Children.Add(SessionButton("Open " + ClaudeControl.Name, () => RunQuickAction(QuickWebApps.ActionId(ClaudeControl.WebAppId), ActionSurface.FullUi)));
        header.Children.Add(buttons);
        header.Children.Add(new TextBlock { Text = "Claude usage", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(header);
        var content = new StackPanel();
        body.Children.Add(content);

        host.Children.Add(new Border
        {
            Child = body, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 12), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left,
        });
        if (control is null) RenderUsageFromFiles(content, path);
        else _ = FillClaudeCardFromControlAsync(content, control, path);
    }

    private static void RenderUsageFromFiles(Panel body, string path)
    {
        try
        {
            ClaudeUsageSnapshot usage = ClaudeUsage.Read(path);
            DateTime written = File.GetLastWriteTime(path);
            bool stale = DateTime.Now - written > TimeSpan.FromHours(26);
            body.Children.Add(new TextBlock
            {
                Text = $"{(usage.PlanName.Length > 0 ? usage.PlanName + " plan · " : "")}limits as of {Friendly(usage.AsOf)} · file written {written:yyyy-MM-dd HH:mm}"
                    + (stale ? " · STALE (the morning refresh has not run)" : ""),
                Foreground = stale ? Brushes.DarkOrange : Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6),
            });
            foreach (ClaudeUsageWindow window in usage.Windows)
            {
                string resets = window.ResetsAt is DateTimeOffset at ? $"resets {at.ToLocalTime():ddd HH:mm}" : "";
                body.Children.Add(UsageRow(window.Label, window.PercentUsed, resets));
            }
            if (usage.Projects.Count > 0)
            {
                body.Children.Add(new TextBlock { Text = "Top projects this week (tokens)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
                foreach (ClaudeUsageProject project in usage.Projects.Take(5))
                    body.Children.Add(new TextBlock { Text = $"{project.Name} — week {ClaudeUsage.Tokens(project.Week)}, today {ClaudeUsage.Tokens(project.Today)}", TextTrimming = TextTrimming.CharacterEllipsis });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            body.Children.Add(new TextBlock { Text = "usage.json could not be read: " + ex.Message, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });
        }
    }

    private static DockPanel UsageRow(string name, double percentUsed, string resets)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        string percent = double.IsNaN(percentUsed) ? "unknown" : $"{percentUsed:0}%";
        var label = new TextBlock { Text = $"{name}: {percent} used {resets}".TrimEnd(), Width = 330, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(label);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = double.IsNaN(percentUsed) ? 0 : percentUsed, Height = 10, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(bar, $"{name} {percent} used");
        row.Children.Add(bar);
        return row;
    }

    private static string Friendly(string stamp) =>
        DateTimeOffset.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : (stamp.Length > 0 ? stamp : "unknown");
}
