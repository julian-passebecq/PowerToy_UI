using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.App.Services;
using JUtility.Core.Actions;
using JUtility.Core.Reports;

namespace JUtility.App;

// V2.1 Mongoku report cards on the Launchpad: fetched only when the user presses Refresh, shown as section
// states and row counts (never row contents), kept in memory only. No MongoDB access. A protected Mongoku's
// user name and password live only in Windows Credential Manager (ReportAuth / WindowsCredentialVault).
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
    private readonly ICredentialVault _vault = new WindowsCredentialVault();

    /// <summary>Saved Mongoku sign-in for an address, or null. Errors reading the vault are reported, never swallowed.</summary>
    private BasicCredential? SavedSignIn(string sourceUrl, out string? problem)
    {
        problem = null;
        try
        {
            return _vault.Read(ReportAuth.Target(sourceUrl));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UriFormatException)
        {
            problem = ex.Message;
            return null;
        }
    }

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
        if (_reportSettings is { } current && !current.Cards.Any(x => MaintenanceReport.Is(x.ReportId)) && current.Cards.Count < ReportCards.MaxCards)
        {
            Button addMaintenance = SessionButton("+ Maintenance card", AddMaintenanceCard);
            addMaintenance.ToolTip = "Adds Mongoku's read-only MAINTENANCE report (what needs attention across the portfolio). Nothing is fetched until you press Refresh.";
            header.Children.Add(addMaintenance);
        }

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
        body.Children.Add(MaintenanceReport.Is(card.ReportId)
            ? LinkText(title, ReportCards.DeepLink(card), card, new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap })
            : new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = $"{card.ReportId} · {new Uri(card.SourceUrl).Authority}", Foreground = Brushes.DimGray, FontSize = 11 });
        if (summary?.NonAuthoritative == true)
        {
            // Declared by Mongoku on the rows; shown even when the user gave the card another title.
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xCE)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xC4, 0x5A)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new TextBlock
                {
                    Text = $"Non-authoritative content ({string.Join(", ", summary.AuthorityBoundaries!.Where(x => x.StartsWith("NON_AUTHORITATIVE", StringComparison.OrdinalIgnoreCase)))}), as declared by Mongoku: not a source of truth.",
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold,
                },
            });
        }

        if (!string.IsNullOrWhiteSpace(summary?.Description))
        {
            // Mongoku's own wording carries each report's caveats (e.g. "not measured evidence", "no live deployment").
            string description = summary.Description.Trim();
            body.Children.Add(new TextBlock
            {
                Text = description.Length > 260 ? description[..257] + "..." : description,
                ToolTip = description,
                Foreground = Brushes.DimGray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        bool maintenance = MaintenanceReport.Is(card.ReportId);
        (string status, Brush color) = loading ? ("Refreshing...", Brushes.DimGray)
            : result is null ? ("Not loaded yet. Press Refresh.", Brushes.DimGray)
            : summary is null && maintenance ? ("Maintenance unavailable: " + (result.Error ?? "unknown error."), Brushes.Firebrick)
            : summary is null ? (result.Error ?? "Unknown error.", Brushes.Firebrick)
            : summary.Maintenance is { } digest ? MaintenanceStatus(digest)
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
            if (summary.Maintenance is { } maintenanceDigest)
            {
                AddMaintenanceBody(body, card, maintenanceDigest);
            }

            // Federation-style reports (e.g. SOURCE_INVENTORY) say per section whether its source resolved.
            List<ReportSectionSummary> traced = summary.Maintenance is null ? summary.Sections.Where(x => x.Resolved is not null).ToList() : [];
            if (traced.Count > 0)
            {
                int resolved = traced.Count(x => x.Resolved == true);
                body.Children.Add(new TextBlock
                {
                    Text = $"Sources resolved: {resolved}/{traced.Count}",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = resolved == traced.Count ? Brushes.SeaGreen : Brushes.Firebrick,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }

            foreach (ReportSectionSummary section in summary.Sections.Take(summary.Maintenance is null ? 8 : 0))
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

            if (summary.Maintenance is null && summary.Sections.Count > 8)
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
        Button open = SessionButton("Open in Mongoku", () => OpenInMongoku(card, ReportCards.DeepLink(card)));
        AutomationProperties.SetName(open, $"Open {spoken} in Mongoku");
        buttons.Children.Add(refresh);
        buttons.Children.Add(open);
        body.Children.Add(buttons);
        var border = new ReportCardBorder { Child = body, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(4) };
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

    private static (string Text, Brush Color) MaintenanceStatus(MaintenanceDigest digest)
    {
        if (digest.SummaryUnavailable) return ("Maintenance summary unavailable (its source did not resolve in Mongoku).", Brushes.Firebrick);
        int missing = digest.UnavailableSections.Count();
        string line = digest.SummaryLine ?? "Mongoku returned no summary line.";
        return missing > 0
            ? ($"{line} ({missing} section{(missing == 1 ? "" : "s")} unavailable)", Brushes.DarkGoldenrod)
            : (line, digest.Actions.Count == 0 ? Brushes.SeaGreen : Brushes.Black);
    }

    /// <summary>
    /// MAINTENANCE: top next action, counts, unavailable sections and the rows that need something, each a link to its
    /// Mongoku page. Read-only: links only open Mongoku; nothing here launches, schedules or changes anything.
    /// </summary>
    private void AddMaintenanceBody(StackPanel body, ReportCard card, MaintenanceDigest digest)
    {
        Uri page = ReportCards.DeepLink(card);
        if (!digest.SummaryUnavailable && !string.IsNullOrWhiteSpace(digest.NextAction))
        {
            body.Children.Add(new TextBlock { Text = "Next: " + digest.NextAction, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }

        body.Children.Add(new TextBlock { Text = MaintenanceReport.CountsLine(digest), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        foreach (MaintenanceSection section in digest.UnavailableSections)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"● {section.Label}: unavailable ({(section.State.Length > 0 ? ReportCards.Explain(section.State) : "source not resolved")}); nothing shown for it.",
                Foreground = Brushes.Firebrick,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        const int shown = 6;
        foreach (MaintenanceRow row in digest.Actions.Take(shown))
        {
            TextBlock link = LinkText($"▸ {row.Title}: {row.NextAction}", row.OpenUri ?? page, card, new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (!string.IsNullOrWhiteSpace(row.Summary)) link.ToolTip = row.Summary;
            body.Children.Add(link);
        }

        int total = digest.Sections.Sum(x => x.ActionRows);
        if (total > shown)
        {
            body.Children.Add(LinkText($"+{total - shown} more to act on in Mongoku", page, card, new TextBlock { Foreground = Brushes.DimGray, Margin = new Thickness(0, 2, 0, 0) }));
        }
        else if (total == 0 && !digest.SummaryUnavailable && !digest.UnavailableSections.Any())
        {
            body.Children.Add(new TextBlock { Text = "Nothing to act on.", Foreground = Brushes.SeaGreen, Margin = new Thickness(0, 2, 0, 0) });
        }
    }

    /// <summary>A text line whose content is a hyperlink to a Mongoku page (opened like "Open in Mongoku").</summary>
    private TextBlock LinkText(string text, Uri target, ReportCard card, TextBlock host)
    {
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { NavigateUri = target };
        link.RequestNavigate += (_, e) => { e.Handled = true; OpenInMongoku(card, target); };
        AutomationProperties.SetName(link, text.Replace('_', ' '));
        AutomationProperties.SetHelpText(link, target.AbsoluteUri);
        host.Inlines.Add(link);
        return host;
    }

    private void AddMaintenanceCard() => SessionAction(() =>
    {
        EnsureReportSettings();
        if (_reportSettings is null || _reportStore is null) return;
        string source = _quickActionSettings?.WebApps.FirstOrDefault(x => x.Name.Contains("Mongoku", StringComparison.OrdinalIgnoreCase))?.Url
            ?? _reportSettings.Cards.FirstOrDefault()?.SourceUrl
            ?? "http://localhost:3100/";
        var candidate = new ReportCardSettings
        {
            Cards = [.. _reportSettings.Cards, new ReportCard { Title = "Maintenance", SourceUrl = source, ReportId = MaintenanceReport.ReportId }],
        };
        try
        {
            _reportStore.Save(candidate);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            ShowOwnedMessage(ex.Message, "Mongoku report cards", MessageBoxImage.Warning);
            return;
        }

        _reportSettings = candidate;
        FillReportPanel();
        _viewModel.StatusText = "Maintenance card added (press Refresh to load it)";
    });

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
            BasicCredential? signIn = SavedSignIn(card.SourceUrl, out string? vaultProblem);
            _reportResults[card.Id] = vaultProblem is not null
                ? new ReportFetchResult(null, vaultProblem)
                : await ReportCards.FetchAsync(card, ReportHttp, signIn);
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

    private void OpenInMongoku(ReportCard card, Uri page)
    {
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

    /// <summary>Asks for a Mongoku user name and password. The password never leaves the PasswordBox except into the vault.</summary>
    private BasicCredential? AskSignIn(string origin)
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = $"Mongoku sign-in for {origin}. Stored in Windows Credential Manager for your Windows account; sent only to {origin}.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock { Text = "User name", Margin = new Thickness(0, 10, 0, 0) });
        var user = new TextBox { MaxLength = 128 };
        AutomationProperties.SetName(user, "User name");
        body.Children.Add(user);
        body.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 6, 0, 0) });
        var password = new PasswordBox { MaxLength = 256, Margin = new Thickness(3) };
        AutomationProperties.SetName(password, "Password");
        body.Children.Add(password);
        var problem = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(problem);
        BasicCredential? result = null;
        Window dialog = SessionDialogWindow("Mongoku sign-in", body);
        dialog.Height = 330;
        body.Children.Add(SessionButton("Save", () =>
        {
            var candidate = new BasicCredential(user.Text.Trim(), password.Password);
            try
            {
                ReportAuth.Validate(candidate);
            }
            catch (InvalidDataException ex)
            {
                problem.Text = ex.Message;
                return;
            }

            result = candidate;
            dialog.DialogResult = true;
        }));
        return SessionDialog(dialog) ? result : null;
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
                BasicCredential? signIn = SavedSignIn(source.Text.Trim(), out _);
                (IReadOnlyList<ReportChoice> choices, string? problem) = await ReportCards.ListReportsAsync(source.Text.Trim(), ReportHttp, signIn);
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

        // Sign-in for a Mongoku protected with MONGOKU_AUTH_BASIC. Stored per address in Windows Credential Manager.
        var signInStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 2) };
        void RefreshSignIn()
        {
            string url = source.Text.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            {
                signInStatus.Text = "Sign-in: enter a valid Mongoku address first.";
                return;
            }

            BasicCredential? saved = SavedSignIn(url, out string? problem);
            string where = ReportAuth.Origin(uri);
            signInStatus.Text = problem
                ?? (saved is null
                    ? $"Sign-in for {where}: none saved. Only needed when that Mongoku uses MONGOKU_AUTH_BASIC."
                    : $"Sign-in for {where}: saved for user '{saved.UserName}' in Windows Credential Manager.")
                + (ReportAuth.RefusalToSend(uri) is string refusal ? " " + refusal : string.Empty);
        }

        source.LostFocus += (_, _) => RefreshSignIn();
        var signInButtons = new WrapPanel();
        signInButtons.Children.Add(SessionButton("Save user and password...", () =>
        {
            string url = source.Text.Trim();
            QuickWebApps.ValidateUrl(url, "Mongoku address");
            if (ReportAuth.RefusalToSend(new Uri(url)) is string refusal) { error.Text = refusal; return; }
            if (AskSignIn(ReportAuth.Origin(new Uri(url))) is not BasicCredential entered) return;
            _vault.Write(ReportAuth.Target(url), entered);
            error.Text = "Sign-in saved in Windows Credential Manager. Refresh a card to use it.";
            RefreshSignIn();
        }));
        signInButtons.Children.Add(SessionButton("Forget sign-in", () =>
        {
            string url = source.Text.Trim();
            QuickWebApps.ValidateUrl(url, "Mongoku address");
            error.Text = _vault.Delete(ReportAuth.Target(url)) ? "Saved sign-in removed from Windows Credential Manager." : "No saved sign-in for this address.";
            RefreshSignIn();
        }));
        body.Children.Add(signInStatus);
        body.Children.Add(signInButtons);
        RefreshSignIn();
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

/// <summary>
/// A plain Border has no UI Automation peer, so its name/help text never reach screen readers. This exposes each
/// report card as a named group ("{title} report card", help text = its status) that assistive tech can navigate.
/// </summary>
internal sealed class ReportCardBorder : Border
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(ReportCardBorder owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Group;

        protected override string GetClassNameCore() => "ReportCard";

        protected override bool IsControlElementCore() => true;
    }
}