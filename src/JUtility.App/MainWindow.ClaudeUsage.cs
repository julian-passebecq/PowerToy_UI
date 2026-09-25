using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;
using JUtility.Core.Reports;

namespace JUtility.App;

// Read-only "Claude usage" card on the Launchpad, from the Effort Board's local usage.json.
// The file is read only when the Launchpad renders or Refresh is pressed; Power Ops never writes to that folder.
public partial class MainWindow
{
    private void RenderClaudeUsageCard(Panel host)
    {
        string path = ClaudeUsage.DefaultPath();
        if (!File.Exists(path)) return; // Optional: no card at all for users without the Effort Board.

        var body = new StackPanel { Margin = new Thickness(12) };
        var header = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Children.Add(SessionButton("Refresh", RefreshLaunchpad));
        WebAppEntry? board = _quickActionSettings?.WebApps.FirstOrDefault(x => x.Name.Contains("Effort Board", StringComparison.OrdinalIgnoreCase));
        if (board is not null) buttons.Children.Add(SessionButton("Open Effort Board", () => RunQuickAction(QuickWebApps.ActionId(board.Id), ActionSurface.FullUi)));
        header.Children.Add(buttons);
        header.Children.Add(new TextBlock { Text = "Claude usage", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(header);

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
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                string percent = double.IsNaN(window.PercentUsed) ? "unknown" : $"{window.PercentUsed:0}%";
                string resets = window.ResetsAt is DateTimeOffset at ? $"resets {at.ToLocalTime():ddd HH:mm}" : "";
                var label = new TextBlock { Text = $"{window.Label}: {percent} used {resets}".TrimEnd(), Width = 330, TextTrimming = TextTrimming.CharacterEllipsis };
                row.Children.Add(label);
                var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = double.IsNaN(window.PercentUsed) ? 0 : window.PercentUsed, Height = 10, MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Automation.AutomationProperties.SetName(bar, $"{window.Label} {percent} used");
                row.Children.Add(bar);
                body.Children.Add(row);
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

        host.Children.Add(new Border
        {
            Child = body, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 12), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left,
        });
    }

    private static string Friendly(string stamp) =>
        DateTimeOffset.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : (stamp.Length > 0 ? stamp : "unknown");
}
