using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;
using Microsoft.Win32;

namespace JUtility.App;

// V2.1 web apps: user-chosen destinations (Mongoku, Grafana, Gemini...) exposed as "web:" quick actions.
// Power Ops only launches them; no browser engine is hosted and nothing is probed in the background.
public partial class MainWindow
{
    private sealed record OpenModeChoice(string Label, WebOpenMode Mode);

    /// <summary>Re-registers one dispatcher action per configured web app. Call after every settings change.</summary>
    private void RegisterWebApps()
    {
        IEnumerable<WebAppEntry> apps = QuickWebApps.All(_quickActionSettings);
        _quickActions.ReplaceDynamic(QuickWebApps.Prefix, apps.Select(app =>
        {
            WebAppEntry captured = app;
            return (QuickWebApps.Definition(captured), new QuickActionHandler(() => OpenWebApp(captured), () => WebAppUnavailable(captured)));
        }).ToList());
        RegisterClaudeControl();
        RegisterRingGroups();
        RefreshToolActions();
        RefreshActionsMenu();
        SyncEmbeddedWeb(); // a web app may have been removed, re-addressed or switched out of Embedded
    }

    private string? WebAppUnavailable(WebAppEntry app) => app.OpenMode switch
    {
        WebOpenMode.AppWindow when AppWindowBrowser(app.Browser) is null =>
            $"{app.Name}: Chrome or Edge was not found for an app window. Switch it to \"Default browser\" in Web apps.",
        WebOpenMode.Embedded when WebView2RuntimeVersion() is null =>
            $"{app.Name}: the Microsoft Edge WebView2 runtime is not installed. Switch it to \"App window\" in Web apps.",
        WebOpenMode.Embedded when _sessionShell is null =>
            $"{app.Name}: workspace tabs did not load, so it cannot open inside Power Ops. Switch it to \"App window\" in Web apps.",
        _ => null,
    };

    private void OpenWebApp(WebAppEntry app)
    {
        QuickWebApps.ValidateUrl(app.Url, app.Name);
        string url = new Uri(app.Url.Trim()).AbsoluteUri;
        if (app.OpenMode == WebOpenMode.Embedded)
        {
            OpenEmbeddedWebApp(app);
            return;
        }

        if (app.OpenMode == WebOpenMode.AppWindow)
        {
            var startInfo = new ProcessStartInfo(AppWindowBrowser(app.Browser)!) { UseShellExecute = false };
            startInfo.ArgumentList.Add("--app=" + url);
            Process.Start(startInfo);
        }
        else
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        _viewModel.StatusText = $"Opened {app.Name}";
    }

    private static string? AppWindowBrowser(WebBrowserChoice choice)
    {
        static string? AppPath(string exe)
        {
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using RegistryKey? key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
            }

            return null;
        }

        using RegistryKey? userChoice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        return QuickWebApps.ChooseAppBrowser(choice, userChoice?.GetValue("ProgId") as string, AppPath("chrome.exe"), AppPath("msedge.exe"));
    }

    private void ManageWebApps() => SessionAction(() =>
    {
        if (_quickActionSettings is null)
        {
            ShowOwnedMessage(ShelfUnavailable()!, "Web apps", MessageBoxImage.Warning);
            return;
        }

        BringToFront();
        List<WebAppEntry> working = QuickActionSettingsStore.Copy(_quickActionSettings).WebApps;
        var removed = new List<Guid>();

        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock
        {
            Text = "Web apps are sites you open often: Mongoku, Grafana, Gemini or a local dev server. Each one becomes an action "
                + "for the Actions menu, the Quick Shelf and global shortcuts. \"App window\" opens it in Chrome or Edge without tabs "
                + "or an address bar, using your existing browser sign-in. Power Ops does not run a browser itself and never checks these sites in the background.",
            TextWrapping = TextWrapping.Wrap,
        });

        var list = new ListBox { MinHeight = 140, Margin = new Thickness(0, 10, 0, 6), DisplayMemberPath = nameof(WebAppEntry.Name) };
        AutomationProperties.SetName(list, "Web apps");
        body.Children.Add(list);

        var name = new TextBox { MaxLength = QuickWebApps.MaxName };
        var url = new TextBox { MaxLength = QuickWebApps.MaxUrl };
        var mode = new ComboBox
        {
            // WPF binds properties, not tuple fields, so use a record.
            ItemsSource = new[]
            {
                new OpenModeChoice("App window (Chrome/Edge)", WebOpenMode.AppWindow),
                new OpenModeChoice("Embedded in Power Ops (local tools, e.g. Mongoku)", WebOpenMode.Embedded),
                new OpenModeChoice("Default browser", WebOpenMode.Browser),
            },
            DisplayMemberPath = nameof(OpenModeChoice.Label),
            SelectedValuePath = nameof(OpenModeChoice.Mode),
        };
        var browser = new ComboBox { ItemsSource = Enum.GetValues<WebBrowserChoice>() };
        AutomationProperties.SetName(name, "Web app name");
        AutomationProperties.SetName(url, "Web app address");
        AutomationProperties.SetName(mode, "Open in");
        AutomationProperties.SetName(browser, "App window browser");
        var editor = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach ((string label, Control control) in new (string, Control)[] { ("Name", name), ("Address", url), ("Open in", mode), ("App window browser", browser) })
        {
            editor.RowDefinitions.Add(new RowDefinition());
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetRow(caption, editor.RowDefinitions.Count - 1);
            Grid.SetRow(control, editor.RowDefinitions.Count - 1);
            Grid.SetColumn(control, 1);
            editor.Children.Add(caption);
            editor.Children.Add(control);
        }

        body.Children.Add(editor);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        bool loading = false;
        void Refresh(WebAppEntry? select = null)
        {
            list.ItemsSource = null;
            list.ItemsSource = working;
            list.SelectedItem = select ?? working.FirstOrDefault();
            editor.IsEnabled = list.SelectedItem is not null;
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not WebAppEntry app) { editor.IsEnabled = false; return; }
            loading = true;
            name.Text = app.Name; url.Text = app.Url; mode.SelectedValue = app.OpenMode; browser.SelectedItem = app.Browser;
            editor.IsEnabled = true;
            loading = false;
        };
        name.TextChanged += (_, _) => { if (!loading && list.SelectedItem is WebAppEntry app) { app.Name = name.Text; list.Items.Refresh(); } };
        url.TextChanged += (_, _) => { if (!loading && list.SelectedItem is WebAppEntry app) app.Url = url.Text.Trim(); };
        mode.SelectionChanged += (_, _) => { if (!loading && list.SelectedItem is WebAppEntry app && mode.SelectedValue is WebOpenMode m) app.OpenMode = m; };
        browser.SelectionChanged += (_, _) => { if (!loading && list.SelectedItem is WebAppEntry app && browser.SelectedItem is WebBrowserChoice b) app.Browser = b; };

        var presets = new ComboBox { ItemsSource = QuickWebApps.Presets, DisplayMemberPath = nameof(WebAppPreset.Name), SelectedIndex = 0, MinWidth = 140 };
        AutomationProperties.SetName(presets, "Preset");
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        buttons.Children.Add(presets);
        buttons.Children.Add(SessionButton("Add preset", () =>
        {
            if (presets.SelectedItem is not WebAppPreset preset) return;
            if (working.Count >= QuickWebApps.MaxWebApps) { error.Text = $"Keep at most {QuickWebApps.MaxWebApps} web apps."; return; }
            WebAppEntry app = QuickWebApps.FromPreset(preset);
            working.Add(app);
            Refresh(app);
            error.Text = preset.Note;
        }));
        buttons.Children.Add(SessionButton("New", () =>
        {
            if (working.Count >= QuickWebApps.MaxWebApps) { error.Text = $"Keep at most {QuickWebApps.MaxWebApps} web apps."; return; }
            var app = new WebAppEntry { Name = "New web app", Url = "https://" };
            working.Add(app);
            Refresh(app);
        }));
        buttons.Children.Add(SessionButton("Remove", () =>
        {
            if (list.SelectedItem is not WebAppEntry app) return;
            working.Remove(app);
            removed.Add(app.Id);
            Refresh();
        }));
        buttons.Children.Add(SessionButton("Test open", () =>
        {
            if (list.SelectedItem is not WebAppEntry app) return;
            try
            {
                string? reason = WebAppUnavailable(app);
                if (reason is not null) { error.Text = reason; return; }
                OpenWebApp(app);
                error.Text = string.Empty;
            }
            catch (Exception ex) when (ex is InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                error.Text = ex.Message;
            }
        }));
        body.Children.Add(buttons);
        body.Children.Add(new TextBlock
        {
            Text = "Removing a web app also removes it from the Quick Shelf and from global shortcuts.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(error);

        Window dialog = SessionDialogWindow("Web apps", body);
        dialog.Height = 640;
        body.Children.Add(SessionButton("Save", () =>
        {
            string? failure = TryUpdateQuickActionSettings(s =>
            {
                foreach (Guid id in removed) QuickWebApps.RemoveWebApp(s, id);
                s.WebApps = working.Select(x => new WebAppEntry { Id = x.Id, Name = x.Name.Trim(), Url = x.Url.Trim(), OpenMode = x.OpenMode, Browser = x.Browser }).ToList();
            });
            if (failure is null) dialog.DialogResult = true;
            else error.Text = failure;
        }));
        Refresh();

        if (SessionDialog(dialog))
        {
            IReadOnlyList<string> failures = ApplyGlobalHotkeys();
            _viewModel.StatusText = $"Web apps saved ({working.Count})";
            if (failures.Count > 0) ShowOwnedMessage(_hotkeyStatus, "Power Ops global shortcuts", MessageBoxImage.Warning);
        }
    });
}
