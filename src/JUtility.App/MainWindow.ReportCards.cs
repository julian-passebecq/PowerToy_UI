using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;
using JUtility.Core.Reports;

namespace JUtility.App;

// V2.1 Mongoku report cards on the Launchpad: fetched only when the user presses Refresh, shown as section
// states and row counts (never row contents), kept in memory only. No MongoDB access, no stored credential.
public partial class MainWindow
{
    private ReportCardStore? _reportStore;
    private ReportCardSettings? _reportSettings;
    private string? _reportLoadError;
    private bool _reportSettingsLoaded;
    private readonly Dictionary<Guid, ReportFetchResult> _reportResults = [];
    private readonly HashSet<Guid> _reportLoading = [];
    private StackPanel? _reportPanel;
    private SocketsHttpHandler? _reportHttp;

    // Created on first Refresh: no sockets or connection pool exist before the user asks for a report.
    private SocketsHttpHandler ReportHttp => _reportHttp ??= new SocketsHttpHandler
    {
        AllowAutoRedirect = false,        // a redirect usually means a sign-in page; report it instead of following it
        UseCookies = false,
        Credentials = null,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
    };

    private void EnsureReportSettings()
    {
        if (_reportSettingsLoaded) return;
        _reportSettingsLoaded = true;
        try
        {
            _reportStore = new ReportCardStore(_viewModel.DataDirectory);
            _reportSettings = _reportStore.Load();
            Closed += (_, _) => _reportHttp?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            _reportSettings = null;
            _reportLoadError = "report-cards.json could not be read, so report cards are off. The file was not changed. " + ex.Message;
        }
    }

    /// <summary>Adds the report section at the top of the Launchpad.</summary>
    private void RenderReportCards(StackPanel launchpad)
    {
        EnsureReportSettings();
        _reportPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        launchpad.Children.Add(_reportPanel);
        FillReportPanel();
    }

    private void FillReportPanel()
    {
        if (_reportPanel is null) return;
        _reportPanel.Children.Clear();
        var header = new WrapPanel();
        header.Children.Add(new TextBlock { Text = "Mongoku reports", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        header.Children.Add(SessionButton("Manage report cards...", ManageReportCards));
        _reportPanel.Children.Add(header);

        if (_reportSettings is null)
        {
            _reportPanel.Children.Add(new TextBlock { Text = _reportLoadError ?? "Report cards are unavailable.", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });
            return;
        }

        _reportPanel.Children.Add(new TextBlock
        {
            Text = _reportSettings.Cards.Count == 0
                ? "Add a card for a Mongoku saved report (for example FOIL status now). Power Ops reads Mongoku's read-only report API only when you press Refresh; it never connects to MongoDB."
                : "On demand only: nothing is fetched until you press Refresh. Section states and row counts only; results are not saved.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 6),
        });

        var cards = new WrapPanel();
        foreach (ReportCard card in _reportSettings.Cards) cards.Children.Add(CreateReportCard(card));
        _reportPanel.Children.Add(cards);
    }

    private Border CreateReportCard(ReportCard card)
    {
        _reportResults.TryGetValue(card.Id, out ReportFetchResult? result);
        ReportSummary? summary = result?.Summary;
        bool loading = _reportLoading.Contains(card.Id);
        var body = new StackPanel { Width = 360, Margin = new Thickness(10) };
        string title = card.Title.Length > 0 ? card.Title : summary?.Title ?? card.ReportId;
        body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = $"{card.ReportId} · {new Uri(card.SourceUrl).Authority}", Foreground = Brushes.DimGray, FontSize = 11 });

        (string status, Brush color) = loading ? ("Refreshing...", Brushes.DimGray)
            : result is null ? ("Not loaded yet. Press Refresh.", Brushes.DimGray)
            : summary is null ? (result.Error ?? "Unknown error.", Brushes.Firebrick)
            : (Overall(summary), HealthBrush(summary.Overall));
        var statusText = new TextBlock { Text = status, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) };
        body.Children.Add(statusText);
        if (summary is not null)
        {
            string generated = summary.GeneratedAt is DateTimeOffset g ? g.ToLocalTime().ToString("HH:mm") : "unknown";
            string readOnly = summary.ReadOnly switch { true => "read-only", false => "NOT marked read-only", null => "read-only flag missing" };
            body.Children.Add(new TextBlock
            {
                Text = $"Generated {generated} by Mongoku · fetched {summary.FetchedAt:HH:mm} · {readOnly}",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (ReportSectionSummary section in summary.Sections.Take(8))
            {
                string rows = section.ReturnedRows is int n ? $"{n} row{(n == 1 ? "" : "s")}" : "rows unknown";
                body.Children.Add(new TextBlock
                {
                    Text = $"● {section.Label}: {rows} · {section.State.ToLowerInvariant().Replace('_', ' ')} ({section.Explanation})",
                    Foreground = HealthBrush(section.Health),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }

            if (summary.Sections.Count > 8)
            {
                body.Children.Add(new TextBlock { Text = $"+{summary.Sections.Count - 8} more sections in Mongoku", Foreground = Brushes.DimGray });
            }
        }

        // WPF treats the first "_" in an automation name as an access-key marker, so report ids are spoken with spaces.
        string spoken = title.Replace('_', ' ');
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Button refresh = SessionButton("Refresh", () => RefreshReportCard(card));
        refresh.IsEnabled = !loading;
        AutomationProperties.SetName(refresh, $"Refresh {spoken}");
        Button open = SessionButton("Open in Mongoku", () => OpenReportInMongoku(card));
        AutomationProperties.SetName(open, $"Open {spoken} in Mongoku");
        buttons.Children.Add(refresh);
        buttons.Children.Add(open);
        body.Children.Add(buttons);
        var border = new Border { Child = body, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(4) };
        AutomationProperties.SetName(border, $"{spoken} report card");
        AutomationProperties.SetHelpText(border, status);
        return border;
    }

    private static string Overall(ReportSummary summary)
    {
        int unavailable = summary.Sections.Count(x => x.Health == SectionHealth.Unavailable);
        int partial = summary.Sections.Count(x => x.Health == SectionHealth.Partial);
        int unknown = summary.Sections.Count(x => x.Health == SectionHealth.Unknown);
        return summary.Overall switch
        {
            SectionHealth.Ok => $"All {summary.Sections.Count} sections complete",
            _ when summary.Sections.Count == 0 => "Mongoku returned no sections (state unknown)",
            _ => string.Join(" · ", new[]
            {
                unavailable > 0 ? $"{unavailable} unavailable" : null,
                partial > 0 ? $"{partial} truncated" : null,
                unknown > 0 ? $"{unknown} unknown" : null,
                $"{summary.Sections.Count(x => x.Health == SectionHealth.Ok)} complete",
            }.Where(x => x is not null)),
        };
    }

    private static Brush HealthBrush(SectionHealth health) => health switch
    {
        SectionHealth.Ok => Brushes.SeaGreen,
        SectionHealth.Partial => Brushes.DarkGoldenrod,
        SectionHealth.Unavailable => Brushes.Firebrick,
        _ => Brushes.DimGray,
    };

    private async void RefreshReportCard(ReportCard card)
    {
        if (!_reportLoading.Add(card.Id)) return;
        FillReportPanel();
        try
        {
            _reportResults[card.Id] = await ReportCards.FetchAsync(card, ReportHttp);
        }
        catch (InvalidDataException ex)
        {
            _reportResults[card.Id] = new ReportFetchResult(null, ex.Message);
        }
        finally
        {
            _reportLoading.Remove(card.Id);
            FillReportPanel();
        }
    }

    private void OpenReportInMongoku(ReportCard card)
    {
        Uri page = ReportCards.DeepLink(card);
        string origin = new Uri(card.SourceUrl).GetLeftPart(UriPartial.Authority);
        WebAppEntry? app = _quickActionSettings?.WebApps.FirstOrDefault(x =>
            Uri.TryCreate(x.Url, UriKind.Absolute, out Uri? uri) && uri.GetLeftPart(UriPartial.Authority).Equals(origin, StringComparison.OrdinalIgnoreCase));
        if (app is not null)
        {
            OpenWebAppAt(app, page); // same mode as the user's Mongoku web app (embedded tab, app window or browser)
            return;
        }

        Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }

    private void ManageReportCards() => SessionAction(() =>
    {
        EnsureReportSettings();
        if (_reportSettings is null || _reportStore is null)
        {
            ShowOwnedMessage(_reportLoadError ?? "Report cards are unavailable.", "Mongoku report cards", MessageBoxImage.Warning);
            return;
        }

        BringToFront();
        var working = _reportSettings.Cards.Select(x => new ReportCard { Id = x.Id, Title = x.Title, SourceUrl = x.SourceUrl, ReportId = x.ReportId }).ToList();
        string defaultSource = _quickActionSettings?.WebApps.FirstOrDefault(x => x.Name.Contains("Mongoku", StringComparison.OrdinalIgnoreCase))?.Url ?? "http://localhost:3100/";

        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "Cards show Mongoku saved reports on the Launchpad. Power Ops only calls Mongoku's read-only report API when you press Refresh, "
                + "shows section states and row counts, and stores no password. If Mongoku requires sign-in, open the report in Mongoku instead.",
            TextWrapping = TextWrapping.Wrap,
        });
        var list = new ListBox { MinHeight = 110, Margin = new Thickness(0, 10, 0, 6), DisplayMemberPath = nameof(ReportCard.ReportId) };
        AutomationProperties.SetName(list, "Report cards");
        body.Children.Add(list);

        body.Children.Add(new TextBlock { Text = "Mongoku address", Margin = new Thickness(0, 6, 0, 0) });
        var source = new TextBox { Text = defaultSource, MaxLength = QuickWebApps.MaxUrl };
        AutomationProperties.SetName(source, "Mongoku address");
        body.Children.Add(source);
        var reports = new ComboBox { Margin = new Thickness(0, 6, 0, 0), MinWidth = 300 };
        AutomationProperties.SetName(reports, "Report");
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 4) };
        var customTitle = new TextBox { MaxLength = 80, ToolTip = "Optional card title (the report's own title is used when empty)" };
        AutomationProperties.SetName(customTitle, "Card title (optional)");
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        Button load = SessionButton("Load report list from Mongoku", () => { });
        load.Click += async (_, _) =>
        {
            load.IsEnabled = false;
            error.Text = "Asking Mongoku for its saved reports...";
            try
            {
                (IReadOnlyList<ReportChoice> choices, string? problem) = await ReportCards.ListReportsAsync(source.Text.Trim(), ReportHttp);
                reports.ItemsSource = choices;
                reports.DisplayMemberPath = nameof(ReportChoice.Title);
                reports.SelectedIndex = choices.Count > 0 ? 0 : -1;
                error.Text = problem ?? $"{choices.Count} reports available.";
            }
            catch (InvalidDataException ex)
            {
                error.Text = ex.Message;
            }
            finally
            {
                load.IsEnabled = true;
            }
        };
        reports.SelectionChanged += (_, _) => description.Text = reports.SelectedItem is ReportChoice c ? $"{c.Id}: {c.Description}" : string.Empty;
        body.Children.Add(load);
        body.Children.Add(reports);
        body.Children.Add(description);
        body.Children.Add(new TextBlock { Text = "Card title (optional)" });
        body.Children.Add(customTitle);

        void Refresh()
        {
            list.ItemsSource = null;
            list.ItemsSource = working;
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        buttons.Children.Add(SessionButton("Add card", () =>
        {
            if (reports.SelectedItem is not ReportChoice choice) { error.Text = "Load the report list and choose a report first."; return; }
            string cardTitle = customTitle.Text.Trim().Length > 0 ? customTitle.Text.Trim() : choice.Title; // readable before the first Refresh
            var card = new ReportCard { Title = cardTitle.Length > 80 ? cardTitle[..80] : cardTitle, SourceUrl = source.Text.Trim(), ReportId = choice.Id };
            try
            {
                ReportCards.Validate(new ReportCardSettings { Cards = [.. working, card] });
            }
            catch (InvalidDataException ex)
            {
                error.Text = ex.Message;
                return;
            }

            working.Add(card);
            customTitle.Text = string.Empty;
            error.Text = $"Added {choice.Title}. Save to keep it.";
            Refresh();
        }));
        buttons.Children.Add(SessionButton("Remove selected", () =>
        {
            if (list.SelectedItem is ReportCard selected) { working.Remove(selected); Refresh(); }
        }));
        body.Children.Add(buttons);
        body.Children.Add(error);

        Window dialog = SessionDialogWindow("Mongoku report cards", body);
        dialog.Height = 620;
        body.Children.Add(SessionButton("Save", () =>
        {
            var candidate = new ReportCardSettings { Cards = working };
            try
            {
                _reportStore.Save(candidate);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
            {
                error.Text = ex.Message;
                return;
            }

            _reportSettings = candidate;
            dialog.DialogResult = true;
        }));
        Refresh();

        if (SessionDialog(dialog))
        {
            foreach (Guid stale in _reportResults.Keys.Where(id => working.All(x => x.Id != id)).ToList()) _reportResults.Remove(stale);
            FillReportPanel();
            _viewModel.StatusText = $"Report cards saved ({working.Count})";
        }
    });
}
