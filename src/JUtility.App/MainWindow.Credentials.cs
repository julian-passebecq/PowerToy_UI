using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.Core.Credentials;
using Microsoft.Win32;

namespace JUtility.App;

// Credentials & IDs module. Metadata (labels, non-secret IDs, .env locations and key names) lives in
// credentials-ids.json; secret values live only in Windows Credential Manager under opaque targets.
// Secret values never enter a TextBlock, status text, automation name, exception message or file written here.
public partial class MainWindow
{
    private const string CredentialsHeader = "Credentials & IDs";
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private readonly ISecretVault _secrets = new WindowsCredentialVault();
    private readonly SecretClipboard _secretClipboard = new();
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private CredentialCatalogStore? _credentialStore;
    private CredentialCatalog? _credentials;
    private string? _credentialLoadError;
    private Guid? _selectedCredentialId;
    private ContentControl? _credentialsHost;
    private Grid? _credentialRecordsView;
    private StackPanel? _credentialList;
    private ContentControl? _credentialDetail;
    private TextBlock? _credentialStatus, _credentialRecordsStatus;
    private TextBox? _revealBox;

    private UIElement CreateCredentialsPage()
    {
        _credentialsHost = new ContentControl();
        _revealTimer.Tick += (_, _) => HideRevealedSecret();
        Closed += (_, _) => { HideRevealedSecret(); _secretClipboard.ClearIfUnchanged(); };
        return _credentialsHost;
    }

    private bool EnsureCredentials()
    {
        if (_credentials is not null) return true;
        _credentialStore ??= new CredentialCatalogStore(_viewModel.DataDirectory);
        try
        {
            _credentials = _credentialStore.Load();
            _credentialLoadError = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            _credentialLoadError = ex.Message;
            return false;
        }
    }

    private void RefreshCredentials()
    {
        if (_credentialsHost is null) return;
        HideRevealedSecret();
        if (!EnsureCredentials())
        {
            _credentialsHost.Content = CredentialLoadErrorView();
            return;
        }

        switch (GetModuleFilter(CredentialsHeader))
        {
            case "env":
                _credentialsHost.Content = EnvRegistryView();
                break;
            case "vault":
                _credentialsHost.Content = VaultCheckView();
                break;
            default:
                _credentialRecordsView ??= CreateCredentialRecordsView();
                _credentialStatus = _credentialRecordsStatus;
                _credentialsHost.Content = _credentialRecordsView;
                RenderCredentialList();
                RenderCredentialDetail();
                break;
        }
    }

    private CredentialView CurrentCredentialView() => GetModuleFilter(CredentialsHeader) switch
    {
        "project" => CredentialView.Project,
        "type" => CredentialView.Type,
        _ => CredentialView.Service,
    };

    private void AddCredentialNavigation(Action<string, string, int> add)
    {
        EnsureCredentials();
        int records = _credentials?.Records.Count ?? 0;
        add("all", "By service", records);
        add("project", "By project", records);
        add("type", "By type", records);
        add("env", "Environments (.env)", _credentials?.EnvFiles.Count ?? 0);
        add("vault", "Vault check", _credentials?.Records.Count(x => CredentialRules.IsSecret(x.Kind)) ?? 0);
    }

    private UIElement CredentialLoadErrorView()
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "Credentials & IDs are unavailable", FontSize = 20, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = $"{_credentialStore?.FilePath} could not be loaded: {_credentialLoadError}\n\nThe file was left untouched and nothing will overwrite it. "
                + "Secrets in Windows Credential Manager are unaffected. Fix or move the file aside, then retry.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8),
        });
        body.Children.Add(SessionButton("Retry", () => { _credentials = null; RefreshCredentials(); RefreshSecondaryNavigation(); }));
        return body;
    }

    // ---------------- Records (by service / project / type) ----------------

    private Grid CreateCredentialRecordsView()
    {
        var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 260 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 240 });

        var toolbar = new WrapPanel();
        toolbar.Children.Add(SessionButton("+ ID", () => AddCredentialRecord(CredentialKind.Id)));
        toolbar.Children.Add(SessionButton("+ Secret / token", () => AddCredentialRecord(CredentialKind.Token)));
        var templates = new ComboBox { ItemsSource = CredentialTemplates.All, DisplayMemberPath = nameof(CredentialTemplates.Template.Service), SelectedIndex = 0, MinWidth = 130, Margin = new Thickness(10, 3, 3, 3) };
        AutomationProperties.SetName(templates, "Service set");
        var templateProject = ProjectPicker("");
        templateProject.MinWidth = 120;
        AutomationProperties.SetName(templateProject, "Project for the service set");
        toolbar.Children.Add(templates);
        toolbar.Children.Add(new TextBlock { Text = "for project", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) });
        toolbar.Children.Add(templateProject);
        toolbar.Children.Add(SessionButton("Add service set", () =>
        {
            if (templates.SelectedItem is not CredentialTemplates.Template template) return;
            IReadOnlyList<CredentialRecord> added = [];
            UpdateCredentials(c => added = CredentialTemplates.Apply(c, template, templateProject.Text));
            if (added.Count > 0) _selectedCredentialId = added[0].Id;
            SetCredentialStatus(added.Count == 0
                ? $"{template.Service} rows already exist for this project; nothing added."
                : $"Added {added.Count} {template.Service} rows with empty values. Fill in the IDs; secrets go through 'Set secret'.");
            RefreshCredentials(); RefreshSecondaryNavigation();
        }));
        _credentialStatus = _credentialRecordsStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(4, 4, 4, 6) };
        var top = new StackPanel();
        top.Children.Add(toolbar);
        top.Children.Add(_credentialStatus);
        Grid.SetColumnSpan(top, 2);
        grid.Children.Add(top);

        _credentialList = new StackPanel();
        var listScroll = new ScrollViewer { Content = _credentialList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(listScroll, 1);
        grid.Children.Add(listScroll);

        _credentialDetail = new ContentControl();
        var detailScroll = new ScrollViewer { Content = _credentialDetail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(detailScroll, 1); Grid.SetColumn(detailScroll, 1);
        grid.Children.Add(detailScroll);
        SetCredentialStatus("IDs copy to the normal clipboard. Secrets are read from Windows Credential Manager only when you copy or reveal them, "
            + $"are excluded from clipboard history and are cleared after {SecretClipboard.ClearAfter.TotalSeconds:0} s.");
        return grid;
    }

    private void SetCredentialStatus(string text)
    {
        if (_credentialStatus is not null) _credentialStatus.Text = text;
        _viewModel.StatusText = text.Length > 120 ? text[..117] + "..." : text;
    }

    private HashSet<string> StoredSecretTargets()
    {
        try
        {
            return _secrets.Targets(CredentialRules.ScopePrefix(_credentials!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Win32Exception ex)
        {
            SetCredentialStatus("Windows Credential Manager could not be listed: " + ex.Message);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void RenderCredentialList()
    {
        if (_credentialList is null || _credentials is null) return;
        _credentialList.Children.Clear();
        var stored = StoredSecretTargets();
        var view = CurrentCredentialView();
        var groups = CredentialRules.Group(_credentials.Records, view, GetModuleSearch(CredentialsHeader));
        if (groups.Count == 0)
        {
            _credentialList.Children.Add(new TextBlock
            {
                Text = _credentials.Records.Count == 0
                    ? "No IDs yet. Add a service set (e.g. Cloudflare for your project) or + ID. Values start empty; nothing is fetched from the service."
                    : "Nothing matches the search.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(4, 10, 4, 4),
            });
            return;
        }

        foreach (var group in groups)
        {
            _credentialList.Children.Add(new TextBlock { Text = $"{group.Name}  ({group.Records.Count})", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 12, 2, 4) });
            foreach (var record in group.Records) _credentialList.Children.Add(CredentialRow(record, view, stored));
        }
    }

    private UIElement CredentialRow(CredentialRecord record, CredentialView view, HashSet<string> stored)
    {
        bool secret = CredentialRules.IsSecret(record.Kind);
        bool hasSecret = secret && stored.Contains(CredentialRules.VaultTarget(_credentials!, record.Id));
        var row = new Grid
        {
            Margin = new Thickness(0, 1, 0, 1),
            Background = record.Id == _selectedCredentialId ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xFB)) : Brushes.Transparent,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string context = view switch
        {
            CredentialView.Project => Join(record.Service, CredentialRules.KindLabel(record.Kind)),
            CredentialView.Service => Join(record.Project, CredentialRules.KindLabel(record.Kind)),
            _ => Join(record.Service, record.Project),
        };
        var labelStack = new StackPanel();
        labelStack.Children.Add(new TextBlock { Text = record.Label, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        labelStack.Children.Add(new TextBlock { Text = context + (record.UsedFor.Length > 0 ? " · " + record.UsedFor.Split('\n')[0] : ""), FontSize = 11, Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis });
        var select = new Button { Content = labelStack, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 3, 6, 3), ToolTip = "Edit" };
        AutomationProperties.SetName(select, $"Edit {record.Label} ({Join(record.Service, record.Project)})");
        select.Click += (_, _) => { _selectedCredentialId = record.Id; RenderCredentialList(); RenderCredentialDetail(); };
        row.Children.Add(select);

        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 6, 0) };
        if (secret)
        {
            value.Text = hasSecret ? "•••••••• in Credential Manager" : "not set";
            value.Foreground = hasSecret ? Brushes.DarkSlateGray : Brushes.DarkOrange;
            AutomationProperties.SetName(value, hasSecret ? "Secret stored in Windows Credential Manager" : "Secret not set");
        }
        else
        {
            value.Text = record.Value.Length == 0 ? "(empty)" : record.Value;
            value.FontFamily = Mono;
            value.Foreground = record.Value.Length == 0 ? Brushes.Gray : Brushes.Black;
            value.ToolTip = record.IsShareable ? "Shareable" : "Private (not included in review exports)";
        }
        Grid.SetColumn(value, 1);
        row.Children.Add(value);

        var copy = new Button { Content = "Copy", Margin = new Thickness(3, 2, 3, 2), Padding = new Thickness(10, 2, 10, 2), IsEnabled = secret ? hasSecret : record.Value.Length > 0 };
        copy.ToolTip = secret ? $"Copy (excluded from clipboard history, cleared after {SecretClipboard.ClearAfter.TotalSeconds:0} s)" : "Copy";
        AutomationProperties.SetName(copy, AccessKeySafe($"Copy {record.Label}" + (record.Service.Length > 0 ? $" ({record.Service})" : "")));
        copy.Click += (_, _) => CopyCredential(record.Id);
        Grid.SetColumn(copy, 2);
        row.Children.Add(copy);
        return row;
    }

    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));

    private CredentialRecord? FindCredential(Guid? id) => id is Guid value ? _credentials?.Records.FirstOrDefault(x => x.Id == value) : null;

    private void CopyCredential(Guid id) => SessionAction(() =>
    {
        if (FindCredential(id) is not CredentialRecord record) return;
        if (!CredentialRules.IsSecret(record.Kind))
        {
            CopyText(record.Value, $"Copied {record.Label}");
            return;
        }

        string? secret = _secrets.ReadSecret(CredentialRules.VaultTarget(_credentials!, record.Id));
        if (string.IsNullOrEmpty(secret))
        {
            SetCredentialStatus($"No secret stored for {record.Label}. Use 'Set secret' first.");
            return;
        }
        _secretClipboard.Copy(secret);
        SetCredentialStatus($"{record.Label} copied. Excluded from clipboard history; cleared in {SecretClipboard.ClearAfter.TotalSeconds:0} s if still on the clipboard.");
    });

    private void AddCredentialRecord(CredentialKind kind) => SessionAction(() =>
    {
        if (!EnsureCredentials() || _credentials is null) { RefreshCredentials(); return; }
        if (GetModuleFilter(CredentialsHeader) is "env" or "vault") _moduleFilters[CredentialsHeader] = "all";
        var context = FindCredential(_selectedCredentialId);
        var record = new CredentialRecord
        {
            Label = CredentialRules.IsSecret(kind) ? "New token" : "New ID",
            Kind = kind,
            Service = context?.Service ?? "",
            Project = context?.Project ?? "",
            SortOrder = _credentials.Records.Count == 0 ? 0 : _credentials.Records.Max(x => x.SortOrder) + 1,
        };
        UpdateCredentials(c =>
        {
            if (c.Records.Count >= CredentialRules.MaxRecords) throw new InvalidOperationException($"Credential record limit ({CredentialRules.MaxRecords}) reached.");
            c.Records.Add(record);
        });
        _selectedCredentialId = record.Id;
        RefreshCredentials(); RefreshSecondaryNavigation();
        if (CredentialRules.IsSecret(kind)) SetSecretValue(record.Id);
    });

    /// <summary>Validates and saves a changed copy; the live catalog is replaced only after the file is written.</summary>
    private void UpdateCredentials(Action<CredentialCatalog> change, bool rotateBackup = false)
    {
        if (_credentials is null || _credentialStore is null) throw new InvalidOperationException(_credentialLoadError ?? "Credentials are not loaded.");
        var copy = CredentialCatalogStore.Copy(_credentials);
        change(copy);
        if (rotateBackup) _credentialStore.SaveAndRotateBackup(copy);
        else _credentialStore.Save(copy);
        _credentials = copy;
    }

    private void RenderCredentialDetail()
    {
        if (_credentialDetail is null) return;
        HideRevealedSecret();
        if (FindCredential(_selectedCredentialId) is not CredentialRecord record)
        {
            _credentialDetail.Content = new TextBlock
            {
                Text = "Select a row to edit it.\n\nIDs (account, zone, project, cluster...) are stored in this data folder as plain metadata. "
                    + "Passwords, tokens and client secrets are stored only in Windows Credential Manager for your Windows account.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(8),
            };
            return;
        }
        _credentialDetail.Content = CredentialEditor(record);
    }

    private ComboBox ProjectPicker(string value)
    {
        var names = (_credentials?.Records.Select(x => x.Project) ?? Enumerable.Empty<string>())
            .Concat(_credentials?.EnvFiles.Select(x => x.Project) ?? Enumerable.Empty<string>())
            .Concat(_viewModel.Projects.Where(x => !x.IsArchived).Select(x => x.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return new ComboBox { IsEditable = true, ItemsSource = names, Text = value, Margin = new Thickness(0, 2, 0, 6) };
    }

    private UIElement CredentialEditor(CredentialRecord record)
    {
        var body = new StackPanel { Margin = new Thickness(8, 4, 4, 8) };
        TextBlock Caption(string text) { var t = new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.DimGray }; body.Children.Add(t); return t; }
        TextBox Field(string caption, string text, int max, bool multiline = false)
        {
            Caption(caption);
            var box = new TextBox { Text = text, MaxLength = max, Margin = new Thickness(0, 2, 0, 6), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 44 : 0 };
            AutomationProperties.SetName(box, caption);
            body.Children.Add(box);
            return box;
        }

        body.Children.Add(new TextBlock { Text = record.Label, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = $"Updated {record.UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · ref {CredentialRules.CredentialRef(record.Id)}", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) });

        var label = Field("Label", record.Label, CredentialRules.MaxLabel);
        Caption("Type");
        var kind = new ComboBox { ItemsSource = Enum.GetValues<CredentialKind>(), SelectedItem = record.Kind, Margin = new Thickness(0, 2, 0, 6), ToolTip = "Password, Token and Secret values are stored in Windows Credential Manager" };
        AutomationProperties.SetName(kind, "Type");
        body.Children.Add(kind);
        Caption("Service / provider");
        var services = (_credentials!.Records.Select(x => x.Service)).Concat(CredentialTemplates.All.Select(x => x.Service))
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var service = new ComboBox { IsEditable = true, ItemsSource = services, Text = record.Service, Margin = new Thickness(0, 2, 0, 6) };
        AutomationProperties.SetName(service, "Service / provider");
        body.Children.Add(service);
        Caption("Project (optional)");
        var project = ProjectPicker(record.Project);
        AutomationProperties.SetName(project, "Project");
        body.Children.Add(project);

        var valueCaption = Caption("Value");
        var value = new TextBox { Text = record.Value, MaxLength = CredentialRules.MaxValue, FontFamily = Mono, Margin = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetName(value, "Value");
        body.Children.Add(value);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
        body.Children.Add(hint);
        var shareable = new CheckBox { Content = "Shareable (the value may appear in review exports)", IsChecked = record.IsShareable, Margin = new Thickness(0, 2, 0, 6) };
        body.Children.Add(shareable);

        var userName = Field("Account / user name (optional, not secret)", record.UserName, CredentialRules.MaxShort);
        var usedFor = Field("Used for (optional)", record.UsedFor, CredentialRules.MaxUsedFor, multiline: true);
        var source = Field("Source / admin URL (optional)", record.SourceUrl, CredentialRules.MaxUrl);

        var secretPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        body.Children.Add(secretPanel);

        void UpdateMode()
        {
            bool secret = kind.SelectedItem is CredentialKind k && CredentialRules.IsSecret(k);
            bool typed = value.Text.Trim().Length > 0;
            valueCaption.Visibility = value.Visibility = !secret || typed ? Visibility.Visible : Visibility.Collapsed;
            shareable.IsEnabled = !secret;
            if (secret) shareable.IsChecked = false;
            string? reason = SecretHeuristics.Reason(value.Text);
            hint.Text = secret && typed
                ? "On Save this value moves into Windows Credential Manager and is removed from Power Ops files."
                : reason is not null ? $"This value {reason}. If it is a secret, change Type to Token/Secret before saving so it goes to Windows Credential Manager." : "";
            secretPanel.Visibility = CredentialRules.IsSecret(record.Kind) && secret ? Visibility.Visible : Visibility.Collapsed;
        }
        kind.SelectionChanged += (_, _) => UpdateMode();
        value.TextChanged += (_, _) => UpdateMode();

        // Secret actions apply to the saved record (its opaque vault target).
        string target = CredentialRules.VaultTarget(_credentials, record.Id);
        bool stored = SafeExists(target);
        secretPanel.Children.Add(new TextBlock
        {
            Text = stored ? "Secret stored in Windows Credential Manager (this Windows account)." : "No secret stored yet.",
            Foreground = stored ? Brushes.DarkSlateGray : Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap,
        });
        var secretButtons = new WrapPanel();
        secretButtons.Children.Add(SessionButton(stored ? "Replace secret..." : "Set secret...", () => SetSecretValue(record.Id)));
        if (stored)
        {
            secretButtons.Children.Add(SessionButton("Copy secret", () => CopyCredential(record.Id)));
            secretButtons.Children.Add(SessionButton("Reveal for 10 s", () => RevealSecret(record.Id)));
            secretButtons.Children.Add(SessionButton("Remove secret", () =>
            {
                if (!ConfirmCredential($"Remove the stored secret for '{record.Label}' from Windows Credential Manager? The row stays, marked 'not set'.")) return;
                _secrets.Delete(target);
                SetCredentialStatus($"Secret for {record.Label} removed from Windows Credential Manager.");
                RenderCredentialList(); RenderCredentialDetail();
            }));
        }
        secretPanel.Children.Add(secretButtons);
        _revealBox = new TextBox { IsReadOnly = true, FontFamily = Mono, Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(_revealBox, "Revealed secret value");
        secretPanel.Children.Add(_revealBox);
        UpdateMode();

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(SessionButton("Save", () => SaveCredentialEdits(record.Id, label.Text, (CredentialKind)kind.SelectedItem, service.Text, project.Text,
            value.Text, userName.Text, usedFor.Text, source.Text, shareable.IsChecked == true)));
        actions.Children.Add(SessionButton("Open source URL", () =>
        {
            if (Uri.TryCreate(source.Text.Trim(), UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https") OpenUrlValue(uri.AbsoluteUri);
            else SetCredentialStatus("Enter an http(s) source / admin URL first.");
        }));
        actions.Children.Add(SessionButton("Delete row", () => DeleteCredential(record.Id)));
        body.Children.Add(actions);
        return body;
    }

    private bool SafeExists(string target)
    {
        try { return _secrets.Exists(target); }
        catch (Win32Exception) { return false; }
    }

    private void SaveCredentialEdits(Guid id, string label, CredentialKind kind, string service, string project, string value,
        string userName, string usedFor, string sourceUrl, bool shareable) => SessionAction(() =>
    {
        if (FindCredential(id) is not CredentialRecord original) return;
        string typed = value.Trim();
        var edited = new CredentialRecord
        {
            Id = original.Id, Label = label.Trim(), Kind = kind, Service = service.Trim(), Project = project.Trim(),
            Value = CredentialRules.IsSecret(kind) ? "" : typed, UserName = userName.Trim(), UsedFor = usedFor.Trim(),
            SourceUrl = sourceUrl.Trim(), IsShareable = !CredentialRules.IsSecret(kind) && shareable,
            SortOrder = original.SortOrder, UpdatedUtc = DateTimeOffset.UtcNow,
        };
        CredentialRules.ValidateRecord(edited);
        string target = CredentialRules.VaultTarget(_credentials!, id);
        bool moveIntoVault = CredentialRules.IsSecret(kind) && typed.Length > 0;
        bool dropSecret = !CredentialRules.IsSecret(kind) && CredentialRules.IsSecret(original.Kind) && SafeExists(target);
        if (moveIntoVault)
        {
            SecretValues.Validate(typed);
            if (SafeExists(target) && !ConfirmCredential($"Replace the secret already stored for '{original.Label}' with the typed value?")) return;
            _secrets.WriteSecret(target, typed, edited.UserName, VaultComment(edited));
        }
        if (dropSecret && !ConfirmCredential($"'{edited.Label}' is no longer a secret type. Remove its stored secret from Windows Credential Manager? (It is not copied into the metadata file.)")) return;

        // A value typed into the plain field and now moved to the vault may still be in the previous .backup: rotate it out.
        UpdateCredentials(c => c.Records[c.Records.FindIndex(x => x.Id == id)] = edited, rotateBackup: moveIntoVault || original.Value.Length > 0 && edited.Value.Length == 0);
        if (dropSecret) _secrets.Delete(target);
        SetCredentialStatus(moveIntoVault ? $"Saved. The value of {edited.Label} is now only in Windows Credential Manager." : $"Saved {edited.Label}.");
        RenderCredentialList(); RenderCredentialDetail(); RefreshSecondaryNavigation();
    });

    private static string VaultComment(CredentialRecord record) => "Power Ops: " + Join(record.Service, record.Project, record.Label);

    private void SetSecretValue(Guid id) => SessionAction(() =>
    {
        if (FindCredential(id) is not CredentialRecord record || !CredentialRules.IsSecret(record.Kind)) return;
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = $"{Join(record.Service, record.Project, record.Label)}\n\nStored in Windows Credential Manager for your Windows account. Power Ops keeps only a reference; the value is never written to its files, exports or logs.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock { Text = "Secret value", Margin = new Thickness(0, 10, 0, 0) });
        var secret = new PasswordBox { MaxLength = SecretValues.MaxLength, Margin = new Thickness(0, 3, 0, 3) };
        AutomationProperties.SetName(secret, "Secret value");
        body.Children.Add(secret);
        var problem = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(problem);
        Window dialog = SessionDialogWindow("Set secret", body);
        dialog.Height = 300;
        body.Children.Add(SessionButton("Save to Credential Manager", () =>
        {
            try
            {
                SecretValues.Validate(secret.Password);
                _secrets.WriteSecret(CredentialRules.VaultTarget(_credentials!, record.Id), secret.Password, record.UserName, VaultComment(record));
                secret.Clear();
                dialog.DialogResult = true;
            }
            catch (Exception ex) when (ex is InvalidDataException or Win32Exception)
            {
                problem.Text = ex.Message;
            }
        }));
        dialog.Loaded += (_, _) => secret.Focus();
        if (SessionDialog(dialog)) SetCredentialStatus($"Secret for {record.Label} saved in Windows Credential Manager.");
        RenderCredentialList(); RenderCredentialDetail();
    });

    private void RevealSecret(Guid id) => SessionAction(() =>
    {
        if (FindCredential(id) is not CredentialRecord record || _revealBox is null) return;
        string? secret = _secrets.ReadSecret(CredentialRules.VaultTarget(_credentials!, record.Id));
        if (string.IsNullOrEmpty(secret)) { SetCredentialStatus("No secret stored."); return; }
        _revealBox.Text = secret;
        _revealBox.Visibility = Visibility.Visible;
        _revealTimer.Stop(); _revealTimer.Start();
        SetCredentialStatus($"{record.Label} revealed for 10 s.");
    });

    private void HideRevealedSecret()
    {
        _revealTimer.Stop();
        if (_revealBox is null) return;
        _revealBox.Clear();
        _revealBox.Visibility = Visibility.Collapsed;
    }

    private void DeleteCredential(Guid id) => SessionAction(() =>
    {
        if (FindCredential(id) is not CredentialRecord record) return;
        string target = CredentialRules.VaultTarget(_credentials!, id);
        bool stored = CredentialRules.IsSecret(record.Kind) && SafeExists(target);
        int links = _credentials!.EnvFiles.SelectMany(x => x.Keys).Count(x => x.CredentialRef == id);
        string linkText = links > 0 ? $" {links} .env key link(s) will be cleared." : "";
        bool removeSecret = false;
        if (stored)
        {
            MessageBoxResult choice = AskCredential(
                $"Delete '{record.Label}'?{linkText}\n\nYes: also remove its secret from Windows Credential Manager.\nNo: keep the secret (it will be listed as orphaned under Vault check).\nCancel: keep everything.",
                MessageBoxButton.YesNoCancel);
            if (choice == MessageBoxResult.Cancel) return;
            removeSecret = choice == MessageBoxResult.Yes;
            if (removeSecret) _secrets.Delete(target); // Vault first: if this fails, the row stays.
        }
        else if (!ConfirmCredential($"Delete '{record.Label}'?{linkText}")) return;

        UpdateCredentials(c => CredentialRules.RemoveRecord(c, id), rotateBackup: record.Value.Length > 0);
        _selectedCredentialId = null;
        SetCredentialStatus(stored && !removeSecret
            ? $"Deleted {record.Label}; its secret was kept in Windows Credential Manager and is listed under Vault check."
            : $"Deleted {record.Label}.");
        RefreshCredentials(); RefreshSecondaryNavigation();
    });

    private bool ConfirmCredential(string message) => AskCredential(message, MessageBoxButton.YesNo) == MessageBoxResult.Yes;

    private MessageBoxResult AskCredential(string message, MessageBoxButton buttons)
    {
        bool previous = _suppressAutoHide; _suppressAutoHide = true;
        try { return MessageBox.Show(this, message, "Power Ops credentials and IDs", buttons, MessageBoxImage.Question, buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel); }
        finally { _suppressAutoHide = previous; }
    }

    // ---------------- .env registry ----------------

    private UIElement EnvRegistryView()
    {
        var body = new StackPanel { Margin = new Thickness(14, 8, 14, 12) };
        body.Children.Add(new TextBlock { Text = "Environments (.env)", FontSize = 20, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = "Tracks where your .env files are and which key NAMES they declare (present / empty / missing). Values are never displayed or saved, "
                + "and Power Ops never edits .env files. Re-scan reads the file on demand; nothing runs in the background.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 6),
        });
        var toolbar = new WrapPanel();
        toolbar.Children.Add(SessionButton("Add .env file...", AddEnvFile));
        toolbar.Children.Add(SessionButton("Add .env files from folder...", AddEnvFolder));
        toolbar.Children.Add(SessionButton("Re-scan all", () => RescanEnv(null)));
        body.Children.Add(toolbar);
        _credentialStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 6) };
        body.Children.Add(_credentialStatus);

        string search = GetModuleSearch(CredentialsHeader);
        var files = _credentials!.EnvFiles
            .Where(f => search.Length == 0 || f.Project.Contains(search, StringComparison.OrdinalIgnoreCase) || f.Path.Contains(search, StringComparison.OrdinalIgnoreCase)
                || f.Environment.Contains(search, StringComparison.OrdinalIgnoreCase) || f.Keys.Any(k => k.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Project.Length == 0 ? 1 : 0).ThenBy(f => f.Project, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Environment, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
            body.Children.Add(new TextBlock { Text = _credentials.EnvFiles.Count == 0 ? "No .env files registered yet." : "Nothing matches the search.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var file in files) body.Children.Add(EnvFileCard(file));
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement EnvFileCard(EnvFileEntry file)
    {
        var card = new StackPanel();
        var border = new Border { BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 6, 0, 4), Child = card };
        card.Children.Add(new TextBlock { Text = Join(file.Project.Length == 0 ? CredentialRules.NoProject : file.Project, file.Environment, Path.GetFileName(file.Path)), FontSize = 15, FontWeight = FontWeights.SemiBold });
        card.Children.Add(new TextBlock { Text = file.Path, FontSize = 11, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
        string observed = file.ObservedUtc is DateTimeOffset at
            ? $"Key names observed {at.ToLocalTime():yyyy-MM-dd HH:mm} · {file.Keys.Count(k => k.State == EnvKeyState.Present)} present, {file.Keys.Count(k => k.State == EnvKeyState.Empty)} empty, {file.Keys.Count(k => k.State == EnvKeyState.Missing)} missing"
            : "Never inspected";
        card.Children.Add(new TextBlock { Text = observed, FontSize = 11, Foreground = Brushes.DimGray });
        if (file.ObservationProblem.Length > 0) card.Children.Add(new TextBlock { Text = file.ObservationProblem, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });

        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        actions.Children.Add(SessionButton("Re-scan", () => RescanEnv(file.Id)));
        actions.Children.Add(SessionButton("Show in folder", () =>
        {
            if (File.Exists(file.Path)) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + file.Path }, UseShellExecute = false });
            else OpenExplorerPath(Path.GetDirectoryName(file.Path), "Folder");
        }));
        actions.Children.Add(SessionButton("Open file", () =>
        {
            if (!File.Exists(file.Path)) throw new FileNotFoundException("The .env file no longer exists at its registered path.");
            Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
        }));
        actions.Children.Add(SessionButton("Edit...", () => EditEnvFile(file.Id)));
        actions.Children.Add(SessionButton("+ Expected key", () => AddExpectedKey(file.Id)));
        actions.Children.Add(SessionButton("Remove from registry", () =>
        {
            if (!ConfirmCredential($"Stop tracking {file.Path}? The file itself is not touched.")) return;
            UpdateCredentials(c => c.EnvFiles.RemoveAll(x => x.Id == file.Id));
            RefreshCredentials(); RefreshSecondaryNavigation();
        }));
        card.Children.Add(actions);

        foreach (var key in file.Keys.OrderBy(k => k.State == EnvKeyState.Missing ? 0 : 1).ThenBy(k => k.Name, StringComparer.Ordinal))
            card.Children.Add(EnvKeyRow(file, key));
        return border;
    }

    private UIElement EnvKeyRow(EnvFileEntry file, EnvKeyEntry key)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = key.Name, FontFamily = Mono, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(name);
        var state = new TextBlock
        {
            Text = key.State.ToString(), VerticalAlignment = VerticalAlignment.Center,
            Foreground = key.State switch { EnvKeyState.Present => Brushes.SeaGreen, EnvKeyState.Missing => Brushes.Firebrick, EnvKeyState.Empty => Brushes.DarkOrange, _ => Brushes.Gray },
            ToolTip = key.Expected ? "Expected key" : "Observed in the file",
        };
        Grid.SetColumn(state, 1); row.Children.Add(state);
        var linked = FindCredential(key.CredentialRef);
        var link = new TextBlock
        {
            Text = linked is null ? "" : "→ " + Join(linked.Service, linked.Label), VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(link, 2); row.Children.Add(link);
        var buttons = new WrapPanel();
        Button Small(string text, string automation, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(6, 1, 6, 1), ToolTip = automation };
            AutomationProperties.SetName(button, AccessKeySafe(automation));
            button.Click += (_, _) => SessionAction(action);
            buttons.Children.Add(button);
            return button;
        }
        Small("Copy name", $"Copy key name {key.Name}", () => CopyText(key.Name, $"Copied {key.Name}"));
        Small(linked is null ? "Link..." : "Relink...", $"Link {key.Name} to a credential", () => LinkEnvKey(file.Id, key.Name));
        if (linked is not null) Small("Copy KEY=value", $"Copy {key.Name}=value from the linked credential", () => CopyEnvAssignment(key.Name, linked.Id));
        var expected = new CheckBox { Content = "Expected", IsChecked = key.Expected, Margin = new Thickness(6, 3, 2, 2), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(expected, $"{key.Name} is expected");
        // Checked/Unchecked rather than Click so the UI Automation Toggle pattern works too.
        void ExpectedChanged(object sender, RoutedEventArgs e) => SessionAction(() =>
        {
            bool value = expected.IsChecked == true;
            UpdateCredentials(c => c.EnvFiles.Single(x => x.Id == file.Id).Keys.Single(x => x.Name == key.Name).Expected = value);
        });
        expected.Checked += ExpectedChanged;
        expected.Unchecked += ExpectedChanged;
        buttons.Children.Add(expected);
        if (key.State is EnvKeyState.Missing or EnvKeyState.Unknown)
            Small("Forget", $"Forget key {key.Name}", () =>
            {
                UpdateCredentials(c => c.EnvFiles.Single(x => x.Id == file.Id).Keys.RemoveAll(x => x.Name == key.Name));
                RefreshCredentials();
            });
        Grid.SetColumn(buttons, 3); row.Children.Add(buttons);
        return row;
    }

    private void CopyEnvAssignment(string keyName, Guid recordId)
    {
        if (FindCredential(recordId) is not CredentialRecord record) return;
        if (!CredentialRules.IsSecret(record.Kind))
        {
            CopyText(EnvRegistry.FormatAssignment(keyName, record.Value), $"Copied {keyName}=...");
            return;
        }
        string? secret = _secrets.ReadSecret(CredentialRules.VaultTarget(_credentials!, record.Id));
        if (string.IsNullOrEmpty(secret)) { SetCredentialStatus($"No secret stored for {record.Label}."); return; }
        _secretClipboard.Copy(EnvRegistry.FormatAssignment(keyName, secret));
        SetCredentialStatus($"{keyName}=… copied for pasting into your .env. Excluded from clipboard history; cleared in {SecretClipboard.ClearAfter.TotalSeconds:0} s.");
    }

    private string? PickEnvFile()
    {
        bool previous = _suppressAutoHide; _suppressAutoHide = true;
        try
        {
            var dialog = new OpenFileDialog { Filter = "Environment files|.env;.env.*;*.env|All files|*.*", CheckFileExists = true, Title = "Choose a .env file (only key names are read)" };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }
        finally { _suppressAutoHide = previous; }
    }

    private void AddEnvFile() => SessionAction(() =>
    {
        if (PickEnvFile() is not string path) return;
        path = Path.GetFullPath(path);
        if (_credentials!.EnvFiles.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            SetCredentialStatus("That .env file is already registered.");
            return;
        }
        var entry = new EnvFileEntry { Path = path, Environment = EnvRegistry.SuggestEnvironment(path), Project = Path.GetFileName(Path.GetDirectoryName(path)) ?? "" };
        if (!EditEnvFields(entry, "Register .env file")) return;
        InspectInto(entry);
        UpdateCredentials(c =>
        {
            if (c.EnvFiles.Count >= CredentialRules.MaxEnvFiles) throw new InvalidOperationException($"At most {CredentialRules.MaxEnvFiles} .env files can be registered.");
            c.EnvFiles.Add(entry);
        });
        SetCredentialStatus($"Registered {Path.GetFileName(path)}: {entry.Keys.Count} key names, no values.");
        RefreshCredentials(); RefreshSecondaryNavigation();
    });

    private void AddEnvFolder() => SessionAction(() =>
    {
        string? folder;
        bool previous = _suppressAutoHide; _suppressAutoHide = true;
        try
        {
            var dialog = new OpenFolderDialog { Title = "Choose a project folder (only .env files directly inside it are added)" };
            folder = dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }
        finally { _suppressAutoHide = previous; }
        if (folder is null) return;
        var known = _credentials!.EnvFiles.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = EnvRegistry.Discover(folder).Select(Path.GetFullPath).Where(x => !known.Contains(x))
            .Select(x => new EnvFileEntry { Path = x, Environment = EnvRegistry.SuggestEnvironment(x), Project = Path.GetFileName(folder.TrimEnd('\\', '/')) })
            .ToList();
        if (entries.Count == 0) { SetCredentialStatus("No new .env files directly inside that folder."); return; }
        foreach (var entry in entries) InspectInto(entry);
        UpdateCredentials(c =>
        {
            if (c.EnvFiles.Count + entries.Count > CredentialRules.MaxEnvFiles) throw new InvalidOperationException($"At most {CredentialRules.MaxEnvFiles} .env files can be registered.");
            c.EnvFiles.AddRange(entries);
        });
        SetCredentialStatus($"Registered {entries.Count} .env file(s): {string.Join(", ", entries.Select(x => Path.GetFileName(x.Path)))}. Key names only.");
        RefreshCredentials(); RefreshSecondaryNavigation();
    });

    private static void InspectInto(EnvFileEntry entry)
    {
        try
        {
            EnvRegistry.Apply(entry, EnvRegistry.Inspect(entry.Path), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Exception messages here name the path or our own size limit, never file content.
            EnvRegistry.MarkUnreadable(entry, "Could not read the file: " + ex.Message);
        }
    }

    private void RescanEnv(Guid? only) => SessionAction(() =>
    {
        int count = 0;
        UpdateCredentials(c =>
        {
            foreach (var entry in c.EnvFiles.Where(x => only is null || x.Id == only)) { InspectInto(entry); count++; }
        });
        SetCredentialStatus($"Re-scanned {count} .env file(s) at {DateTime.Now:HH:mm:ss}. Key names only.");
        RefreshCredentials();
    });

    private void EditEnvFile(Guid id) => SessionAction(() =>
    {
        var working = CredentialCatalogStore.Copy(_credentials!).EnvFiles.Single(x => x.Id == id);
        if (!EditEnvFields(working, "Edit .env entry")) return;
        UpdateCredentials(c =>
        {
            var entry = c.EnvFiles.Single(x => x.Id == id);
            entry.Project = working.Project; entry.Environment = working.Environment;
        });
        RefreshCredentials();
    });

    private bool EditEnvFields(EnvFileEntry entry, string title)
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = entry.Path, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        body.Children.Add(new TextBlock { Text = "Project (optional)", Margin = new Thickness(0, 10, 0, 0) });
        var project = ProjectPicker(entry.Project);
        AutomationProperties.SetName(project, "Project");
        body.Children.Add(project);
        body.Children.Add(new TextBlock { Text = "Environment label (local, dev, preview, production...)" });
        var environment = new ComboBox { IsEditable = true, ItemsSource = new[] { "local", "dev", "preview", "staging", "production", "test", "example" }, Text = entry.Environment, Margin = new Thickness(0, 2, 0, 6) };
        AutomationProperties.SetName(environment, "Environment label");
        body.Children.Add(environment);
        var problem = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(problem);
        Window dialog = SessionDialogWindow(title, body);
        dialog.Height = 300;
        body.Children.Add(SessionButton("Save", () =>
        {
            string env = environment.Text.Trim(), proj = project.Text.Trim();
            if (env.Length is 0 or > 40 || proj.Length > CredentialRules.MaxShort) { problem.Text = "Environment label: 1-40 characters. Project: up to 120."; return; }
            entry.Environment = env; entry.Project = proj;
            dialog.DialogResult = true;
        }));
        return SessionDialog(dialog);
    }

    private void AddExpectedKey(Guid fileId) => SessionAction(() =>
    {
        string? name = AskSessionName("Expected key name (e.g. CLOUDFLARE_API_TOKEN). Only the name is stored.", "");
        if (name is null) return;
        if (!CredentialRules.IsValidKeyName(name)) throw new InvalidDataException("Key names use letters, digits, '_', '.' or '-' and start with a letter or '_'.");
        UpdateCredentials(c =>
        {
            var entry = c.EnvFiles.Single(x => x.Id == fileId);
            if (entry.Keys.FirstOrDefault(x => x.Name == name) is EnvKeyEntry existing) { existing.Expected = true; return; }
            if (entry.Keys.Count >= CredentialRules.MaxKeysPerFile) throw new InvalidOperationException("Too many keys for this .env entry.");
            entry.Keys.Add(new EnvKeyEntry { Name = name, Expected = true, State = entry.ObservedUtc is null ? EnvKeyState.Unknown : EnvKeyState.Missing });
        });
        RefreshCredentials();
    });

    private void LinkEnvKey(Guid fileId, string keyName) => SessionAction(() =>
    {
        var file = _credentials!.EnvFiles.Single(x => x.Id == fileId);
        var current = file.Keys.Single(x => x.Name == keyName).CredentialRef;
        var choices = _credentials.Records
            .OrderBy(x => string.Equals(x.Project, file.Project, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Service, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Select(x => new LinkChoice(x.Id, $"{Join(x.Service, x.Label)}  [{CredentialRules.KindLabel(x.Kind)}]{(x.Project.Length > 0 ? "  · " + x.Project : "")}"))
            .ToList();
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = $"Link {keyName} to a credential or ID. Only an opaque reference is stored.", TextWrapping = TextWrapping.Wrap });
        var list = new ListBox { ItemsSource = choices, DisplayMemberPath = nameof(LinkChoice.Text), Height = 260, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetName(list, "Credentials");
        list.SelectedItem = choices.FirstOrDefault(x => x.Id == current);
        body.Children.Add(list);
        Guid? chosen = current;
        Window dialog = SessionDialogWindow("Link " + keyName, body);
        var buttons = new WrapPanel();
        buttons.Children.Add(SessionButton("Link", () => { if (list.SelectedItem is LinkChoice c) { chosen = c.Id; dialog.DialogResult = true; } }));
        buttons.Children.Add(SessionButton("Unlink", () => { chosen = null; dialog.DialogResult = true; }));
        body.Children.Add(buttons);
        if (!SessionDialog(dialog)) return;
        UpdateCredentials(c => c.EnvFiles.Single(x => x.Id == fileId).Keys.Single(x => x.Name == keyName).CredentialRef = chosen);
        RefreshCredentials();
    });

    private sealed record LinkChoice(Guid Id, string Text)
    {
        public override string ToString() => Text; // List items are announced by ToString.
    }

    /// <summary>
    /// WPF strips the first '_' (access-key marker) from the automation name of a button with text content,
    /// so FOO_API_TOKEN would be announced as FOOAPI_TOKEN. Doubled underscores survive as one.
    /// </summary>
    private static string AccessKeySafe(string name) => name.Replace("_", "__");

    // ---------------- Vault check ----------------

    private UIElement VaultCheckView()
    {
        var body = new StackPanel { Margin = new Thickness(14, 8, 14, 12) };
        body.Children.Add(new TextBlock { Text = "Vault check", FontSize = 20, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock
        {
            Text = "Compares this data folder's secret rows with its entries in Windows Credential Manager (Control Panel → Credential Manager → Windows Credentials, "
                + $"targets starting with {CredentialRules.VaultPrefix}). No secret value is read for this check.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 8),
        });
        _credentialStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
        body.Children.Add(_credentialStatus);

        IReadOnlyList<string> all;
        try { all = _secrets.Targets(CredentialRules.VaultPrefix); }
        catch (Win32Exception ex)
        {
            body.Children.Add(new TextBlock { Text = "Could not list Windows Credential Manager: " + ex.Message, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });
            return body;
        }
        var secrets = _credentials!.Records.Where(x => CredentialRules.IsSecret(x.Kind)).ToList();
        var mine = all.Where(x => x.StartsWith(CredentialRules.ScopePrefix(_credentials), StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = secrets.Where(x => !mine.Contains(CredentialRules.VaultTarget(_credentials, x.Id))).ToList();
        var orphans = CredentialRules.OrphanTargets(_credentials, all);
        int otherFolders = all.Count - mine.Count;

        body.Children.Add(new TextBlock { Text = $"{secrets.Count} secret rows · {secrets.Count - missing.Count} stored · {missing.Count} not set · {orphans.Count} orphaned · {otherFolders} belong to other Power Ops data folders (never touched here)", Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap });
        if (missing.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = "Not set", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            foreach (var record in missing)
            {
                var row = new WrapPanel();
                row.Children.Add(new TextBlock { Text = Join(record.Service, record.Project, record.Label), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(SessionButton("Set secret...", () => { SetSecretValue(record.Id); RefreshCredentials(); }));
                body.Children.Add(row);
            }
        }
        if (orphans.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = "Orphaned (no row points to them)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
            foreach (string target in orphans)
            {
                var row = new WrapPanel();
                row.Children.Add(new TextBlock { Text = target, FontFamily = Mono, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                row.Children.Add(SessionButton("Remove from Credential Manager", () =>
                {
                    if (!ConfirmCredential($"Permanently remove {target} from Windows Credential Manager? This cannot be undone.")) return;
                    _secrets.Delete(target);
                    RefreshCredentials(); RefreshSecondaryNavigation();
                }));
                body.Children.Add(row);
            }
        }
        if (missing.Count == 0 && orphans.Count == 0) body.Children.Add(new TextBlock { Text = "Every secret row has a stored secret and there are no orphans.", Foreground = Brushes.SeaGreen, Margin = new Thickness(0, 8, 0, 0) });
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
