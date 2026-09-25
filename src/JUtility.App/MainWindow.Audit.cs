using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JUtility.Core.Services;

namespace JUtility.App;

public partial class MainWindow
{
    private readonly WorkflowAuditReader _auditReader = WorkflowAuditReader.CreateDefault();
    private WorkflowAuditReport? _auditReport;
    private bool _auditReadRunning;

    private async Task RefreshAuditCardAsync()
    {
        if (_auditReadRunning)
        {
            return;
        }

        _auditReadRunning = true;
        try
        {
            (WorkflowAuditReport? report, string? logLine) = await Task.Run(() => (_auditReader.ReadLatest(), _auditReader.ReadLastLogLine()));
            _auditReport = report;
            RenderAuditCard(logLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AuditMetaText.Text = "Could not read audits: " + ex.Message;
        }
        finally
        {
            _auditReadRunning = false;
        }
    }

    private void RenderAuditCard(string? logLine)
    {
        WorkflowAuditReport? report = _auditReport;
        AuditOpenButton.IsEnabled = report is not null;
        AuditCard.ToolTip = logLine is null ? _auditReader.AuditsDirectory : "Last run: " + logLine;
        if (report is null)
        {
            SetAuditBadge("No report", "StatusIdleBrush");
            AuditStaleBadge.Visibility = Visibility.Collapsed;
            AuditMetaText.Text = string.Empty;
            AuditSummaryText.Text = $"No audit report yet in {_auditReader.AuditsDirectory}. Use Checkup to run \"{WorkflowAuditReader.ScheduledTaskName}\".";
            return;
        }

        (string label, string brushKey) = report.Status switch
        {
            WorkflowAuditStatus.Ok => ("OK", "StatusOkBrush"),
            WorkflowAuditStatus.Warning => ("Warning", "StatusAttentionBrush"),
            WorkflowAuditStatus.Critical => ("Critical", "StatusCriticalBrush"),
            _ => ("Unknown", "StatusIdleBrush"),
        };
        SetAuditBadge(label, brushKey);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        AuditStaleBadge.Visibility = report.IsStale(now) ? Visibility.Visible : Visibility.Collapsed;
        string problems = report.ProblemCount == 1 ? "1 problem" : $"{report.ProblemCount} problems";
        AuditMetaText.Text = $"{problems} · {report.Date:yyyy-MM-dd} {report.Slot} · {FormatAuditAgo(now - report.RunAt)} ago";
        AuditSummaryText.Text = report.Summary.Count == 0 ? "(no Résumé section)" : string.Join(Environment.NewLine, report.Summary);
    }

    private void SetAuditBadge(string text, string brushKey)
    {
        AuditStatusText.Text = text;
        AuditStatusBadge.SetResourceReference(Border.BackgroundProperty, brushKey); // Follows light/dark switches.
    }

    internal static string FormatAuditAgo(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "<1m";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        return $"{(int)span.TotalDays}d {span.Hours}h";
    }

    private void AuditOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_auditReport is not { } report)
        {
            return;
        }

        // Re-read so the viewer never shows a report the audit task has since rewritten.
        string markdown = report.Markdown;
        try
        {
            using FileStream stream = new(report.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            markdown = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the cached copy.
        }

        FlowDocumentScrollViewer viewer = new()
        {
            Document = MarkdownToFlowDocument(markdown),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Button openExternal = new() { Content = "Open in default app", Margin = new Thickness(0, 0, 8, 0) };
        openExternal.Click += (_, _) => OpenAuditFileExternally(report.FilePath);
        Button close = new() { Content = "Close", IsCancel = true };

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        buttons.Children.Add(openExternal);
        buttons.Children.Add(close);
        DockPanel root = new();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(viewer);

        Window window = new()
        {
            Title = "Audit — " + Path.GetFileName(report.FilePath),
            Owner = this,
            Width = 820,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = root,
        };
        window.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        App.Theme?.Track(window);
        close.Click += (_, _) => window.Close();
        window.Show();
    }

    private void OpenAuditFileExternally(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No .md association: Notepad is always there.
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private void AuditCheckup_Click(object sender, RoutedEventArgs e)
    {
        // The Claude desktop app has no URL or CLI to run a scheduled task now (run_scheduled_task is
        // only reachable from inside the app), so open the task page and leave the click to the user.
        try
        {
            Process.Start(new ProcessStartInfo(WorkflowAuditReader.ScheduledTaskDeepLink) { UseShellExecute = true });
            _viewModel.StatusText = $"Opened Claude — Scheduled → {WorkflowAuditReader.ScheduledTaskName} → Run now.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _viewModel.StatusText = "Could not open Claude: " + ex.Message;
            return;
        }

        if (sender is Button button && button.ToolTip is not null)
        {
            ToolTip tip = button.ToolTip as ToolTip ?? new ToolTip { Content = button.ToolTip };
            button.ToolTip = tip;
            tip.PlacementTarget = button;
            tip.IsOpen = true;
            System.Windows.Threading.DispatcherTimer hide = new() { Interval = TimeSpan.FromSeconds(6) };
            hide.Tick += (_, _) =>
            {
                hide.Stop();
                tip.IsOpen = false;
            };
            hide.Start();
        }
    }

    internal static FlowDocument MarkdownToFlowDocument(string markdown)
    {
        FlowDocument document = new()
        {
            FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji"),
            FontSize = 13,
            PagePadding = new Thickness(24, 16, 24, 16),
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
        document.SetResourceReference(FlowDocument.BackgroundProperty, "SurfaceBrush");
        List? list = null;
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                list = null;
                continue;
            }

            Match heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                list = null;
                int level = heading.Groups[1].Length;
                Paragraph h = new(AppendInlines(new Span(), heading.Groups[2].Value))
                {
                    FontSize = level switch { 1 => 22, 2 => 17, _ => 14 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, level == 1 ? 0 : 14, 0, 4),
                };
                document.Blocks.Add(h);
                continue;
            }

            Match item = Regex.Match(line, @"^(\s*)(?:[-*+]|\d+[.)])\s+(.*)$");
            if (item.Success)
            {
                if (list is null)
                {
                    list = new List { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(20, 0, 0, 0) };
                    document.Blocks.Add(list);
                }

                Paragraph p = new(AppendInlines(new Span(), item.Groups[2].Value))
                {
                    Margin = new Thickness(item.Groups[1].Length * 6, 1, 0, 1),
                };
                list.ListItems.Add(new ListItem(p));
                continue;
            }

            list = null;
            document.Blocks.Add(new Paragraph(AppendInlines(new Span(), line.TrimStart('>', ' '))) { Margin = new Thickness(0, 2, 0, 4) });
        }

        return document;
    }

    private static Span AppendInlines(Span span, string text)
    {
        // **bold** and `code`; everything else stays literal.
        foreach (string part in Regex.Split(text, @"(\*\*[^*]+\*\*|`[^`]+`)"))
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal) && part.Length > 4)
            {
                span.Inlines.Add(new Bold(new Run(part[2..^2])));
            }
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
            {
                Run code = new(part[1..^1]) { FontFamily = new FontFamily("Consolas") };
                code.SetResourceReference(TextElement.BackgroundProperty, "ChipBrush");
                span.Inlines.Add(code);
            }
            else
            {
                span.Inlines.Add(new Run(part));
            }
        }

        return span;
    }
}
