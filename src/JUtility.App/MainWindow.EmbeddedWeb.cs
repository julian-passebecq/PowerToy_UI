using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;
using JUtility.Core.Workspaces;
using Microsoft.Win32;

namespace JUtility.App;

// V2.1 embedded web apps ("Web" module). This file never references WebView2 types: EmbeddedWebHost is created
// on the first embedded open, so Power Ops starts and idles exactly as before for users who never embed anything.
public partial class MainWindow
{
    private readonly Grid _webRoot = new();
    private readonly StackPanel _webPicker = new() { Margin = new Thickness(18) };
    private EmbeddedWebHost? _webHost;
    // Deep links requested for an embedded app (e.g. "Open in Mongoku" on a report card), consumed on show.
    private readonly Dictionary<string, string> _pendingEmbeddedNavigation = new(StringComparer.Ordinal);

    private UIElement CreateWebPage()
    {
        _webRoot.Children.Add(_webPicker);
        return _webRoot;
    }

    private static string? WebView2RuntimeVersion()
    {
        const string client = @"\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        foreach ((RegistryKey hive, string path) in new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node" + client),
            (Registry.LocalMachine, "SOFTWARE" + client),
            (Registry.CurrentUser, "Software" + client),
        })
        {
            using RegistryKey? key = hive.OpenSubKey(path);
            if (key?.GetValue("pv") is string version && version != "0.0.0.0" && version.Length > 0) return version;
        }

        return null;
    }

    private void OpenEmbeddedWebApp(WebAppEntry app)
    {
        if (_sessionShell is null)
        {
            throw new InvalidOperationException("Workspace tabs are unavailable, so the web app cannot open inside Power Ops. Switch it to App window in Web apps.");
        }

        BringToFront();
        RememberSession();
        EmbeddedWebPolicy.ShowInWorkspace(CurrentWorkspace(), QuickWebApps.ActionId(app.Id));
        RestoreSession();
        MarkSessionDirty();
    }

    /// <summary>Called at the end of every session restore: show the active web tab and drop views no longer open.</summary>
    private void SyncEmbeddedWeb()
    {
        if (_sessionShell is null) return;
        WorkspaceProfile workspace = CurrentWorkspace();
        _webHost?.Retain(EmbeddedWebPolicy.LiveViews(workspace, _quickActionSettings));
        SessionTab tab = WorkspaceSessions.ActiveTab(workspace);
        if (tab.ModuleId != EmbeddedWebPolicy.ModuleId) return;

        string? actionId = EmbeddedWebPolicy.TabWebApp(tab);
        WebAppEntry? app = QuickWebApps.Find(_quickActionSettings, actionId);
        if (actionId is null || app is null || app.OpenMode != WebOpenMode.Embedded)
        {
            ShowWebPicker(actionId is null ? null : app is null
                ? "This tab's web app no longer exists."
                : $"{app.Name} is no longer set to Embedded. Open it from the list or change it in Web apps.");
            return;
        }

        ShowEmbedded(actionId, app);
    }

    private async void ShowEmbedded(string actionId, WebAppEntry app)
    {
        if (WebView2RuntimeVersion() is null)
        {
            ShowWebPicker("The Microsoft Edge WebView2 runtime is not installed, so web apps cannot open inside Power Ops. Use App window instead.");
            return;
        }

        if (_webHost is null)
        {
            _webHost = new EmbeddedWebHost(EmbeddedWebPolicy.UserDataFolder(_viewModel.DataDirectory), uri => SavedSignIn(uri.AbsoluteUri, out _));
            _webHost.OpenOutsideRequested += (_, entry) => OpenWebAppOutside(entry);
            _webHost.CloseRequested += (_, id) => { _webHost.Remove(id); ShowWebPicker($"The web view was closed and its memory released. Click {QuickWebApps.Find(_quickActionSettings, id)?.Name ?? "it"} to reopen."); };
            _webRoot.Children.Add(_webHost);
            Closed += (_, _) => _webHost.DisposeAll();
        }

        _webPicker.Visibility = Visibility.Collapsed;
        _webHost.Visibility = Visibility.Visible;
        CurrentModuleTitle.Text = app.Name;
        CurrentModuleSubtitle.Text = "Embedded web app · " + app.Url;
        try
        {
            _pendingEmbeddedNavigation.Remove(actionId, out string? navigateTo);
            await _webHost.ShowAsync(actionId, app, navigateTo);
        }
        catch (Exception ex)
        {
            _webHost.Remove(actionId);
            ShowWebPicker($"{app.Name} could not open inside Power Ops: {ex.Message}");
        }
    }

    /// <summary>Opens a page of a configured web app in that app's own mode (embedded tab, app window or browser).</summary>
    private void OpenWebAppAt(WebAppEntry app, Uri page)
    {
        if (app.OpenMode == WebOpenMode.Embedded)
        {
            _pendingEmbeddedNavigation[QuickWebApps.ActionId(app.Id)] = page.AbsoluteUri;
            OpenEmbeddedWebApp(app);
            return;
        }

        OpenWebApp(new WebAppEntry { Id = app.Id, Name = app.Name, Url = page.AbsoluteUri, OpenMode = app.OpenMode, Browser = app.Browser });
    }

    private void OpenWebAppOutside(WebAppEntry app)
    {
        var outside = new WebAppEntry { Id = app.Id, Name = app.Name, Url = app.Url, Browser = app.Browser, OpenMode = AppWindowBrowser(app.Browser) is null ? WebOpenMode.Browser : WebOpenMode.AppWindow };
        OpenWebApp(outside);
    }

    private void ShowWebPicker(string? message)
    {
        if (_webHost is not null) _webHost.Visibility = Visibility.Collapsed;
        _webPicker.Visibility = Visibility.Visible;
        _webPicker.Children.Clear();
        CurrentModuleTitle.Text = "Web apps";
        CurrentModuleSubtitle.Text = "Embedded web apps open here; WebView2 starts only when one is opened.";
        _webPicker.Children.Add(new TextBlock { Text = "Embedded web apps", FontSize = 22, FontWeight = FontWeights.SemiBold });
        if (message is not null)
        {
            _webPicker.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGoldenrod, Margin = new Thickness(0, 8, 0, 4) });
        }

        List<WebAppEntry> embedded = QuickWebApps.All(_quickActionSettings).Where(x => x.OpenMode == WebOpenMode.Embedded).ToList();
        _webPicker.Children.Add(new TextBlock
        {
            Text = embedded.Count == 0
                ? "No web app is set to \"Embedded in Power Ops\". Use it for local or self-hosted tools such as Mongoku (http://localhost:3100). "
                    + "Keep AI chats and Google-login sites in App window mode: they use your normal browser sign-in."
                : "Each opens in its own Power Ops tab with a separate browser profile stored in the Power Ops data folder. Pop-ups open in your normal browser.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 8),
        });
        foreach (WebAppEntry app in embedded)
        {
            string id = QuickWebApps.ActionId(app.Id);
            Button open = SessionButton($"{app.Name}  ({app.Url})", () => RunQuickAction(id, ActionSurface.FullUi));
            open.HorizontalAlignment = HorizontalAlignment.Left;
            _webPicker.Children.Add(open);
        }

        Button manage = SessionButton("Web apps...", ManageWebApps);
        manage.HorizontalAlignment = HorizontalAlignment.Left;
        manage.Margin = new Thickness(3, 12, 3, 3);
        _webPicker.Children.Add(manage);
    }
}
