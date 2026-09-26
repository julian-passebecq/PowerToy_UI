using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JUtility.Core.Actions;
using JUtility.Core.Reports;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace JUtility.App;

// The only type that touches WebView2. MainWindow creates it on the first embedded open, so the WebView2
// assembly, environment and browser processes do not exist until then. One shared environment; one view per
// embedded web app that the current workspace still has open (see EmbeddedWebPolicy.LiveViews).
// The standard (HwndHost) control is used: WebView2CompositionControl needs the Windows SDK projection
// (Microsoft.Windows.SDK.NET, a Windows-10 TFM, ~25 MB) and crashed in layout without it. Airspace is not an issue:
// nothing in MainWindow overlays the module area, and the Shelf, Ring, menus and pop-ups are separate windows.
internal sealed class EmbeddedWebHost : DockPanel
{
    private readonly string _userDataFolder;
    private readonly Func<Uri, BasicCredential?> _signInFor;
    private readonly Dictionary<string, int> _signInAttempts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (WebView2 View, WebAppEntry App)> _views = new(StringComparer.Ordinal);
    private readonly Grid _viewArea = new();
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    private readonly TextBlock _address = new() { Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { Foreground = Brushes.DimGray, Margin = new Thickness(6, 2, 6, 2), TextWrapping = TextWrapping.Wrap };
    private CoreWebView2Environment? _environment;
    private string? _current;

    /// <param name="signInFor">Saved basic-auth sign-in for an address (Windows Credential Manager), or null.</param>
    public EmbeddedWebHost(string userDataFolder, Func<Uri, BasicCredential?> signInFor)
    {
        _userDataFolder = userDataFolder;
        _signInFor = signInFor;
        var toolbar = new DockPanel { Margin = new Thickness(4) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(ToolbarButton("", "Back", () => Current()?.GoBack()));
        left.Children.Add(ToolbarButton("", "Forward", () => Current()?.GoForward()));
        left.Children.Add(ToolbarButton("", "Reload", () => Current()?.Reload()));
        left.Children.Add(_title);
        DockPanel.SetDock(left, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var outside = new Button { Content = "Open outside", Margin = new Thickness(3), ToolTip = "Open this page in an app window / your browser" };
        outside.Click += (_, _) => { if (_current is not null && _views.TryGetValue(_current, out var v)) OpenOutsideRequested?.Invoke(this, v.App); };
        var close = new Button { Content = "Close web view", Margin = new Thickness(3), ToolTip = "Stop this page and free its memory (the tab stays; reopening reloads it)" };
        close.Click += (_, _) => { if (_current is not null) CloseRequested?.Invoke(this, _current); };
        AutomationProperties.SetName(outside, "Open outside");
        AutomationProperties.SetName(close, "Close web view");
        right.Children.Add(outside);
        right.Children.Add(close);
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(left);
        toolbar.Children.Add(right);
        toolbar.Children.Add(_address);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        Children.Add(toolbar);
        Children.Add(_status);
        Children.Add(_viewArea);
    }

    public event EventHandler<WebAppEntry>? OpenOutsideRequested;
    public event EventHandler<string>? CloseRequested;

    public int LiveViewCount => _views.Count;

    /// <param name="navigateTo">Optional deep link (e.g. a Mongoku report page) inside the same app.</param>
    public async Task ShowAsync(string actionId, WebAppEntry app, string? navigateTo = null)
    {
        if (navigateTo is not null && !EmbeddedWebPolicy.AllowNavigation(navigateTo)) navigateTo = null;
        _current = actionId;
        if (_views.TryGetValue(actionId, out var existing) && existing.App.Url != app.Url)
        {
            Remove(actionId); // address edited in Web apps: start over on the new URL
        }

        if (!_views.ContainsKey(actionId))
        {
            _status.Text = $"Starting the embedded view for {app.Name}...";
            _environment ??= await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
            var view = new WebView2();
            AutomationProperties.SetName(view, app.Name + " web view");
            _views[actionId] = (view, app);
            _viewArea.Children.Add(view);
            await view.EnsureCoreWebView2Async(_environment);
            Configure(actionId, view.CoreWebView2);
            view.CoreWebView2.Navigate(navigateTo ?? new Uri(app.Url.Trim()).AbsoluteUri);
        }
        else if (navigateTo is not null)
        {
            _views[actionId].View.CoreWebView2?.Navigate(navigateTo);
        }

        foreach ((string id, (WebView2 view, _)) in _views)
        {
            view.Visibility = id == actionId ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateToolbar();
    }

    /// <summary>Disposes every view the policy no longer keeps; the environment goes when the last view does.</summary>
    public void Retain(IReadOnlySet<string> live)
    {
        foreach (string id in _views.Keys.Where(id => !live.Contains(id)).ToList()) Remove(id);
    }

    public void Remove(string actionId)
    {
        if (!_views.Remove(actionId, out var entry)) return;
        _viewArea.Children.Remove(entry.View);
        entry.View.Dispose();
        if (_current == actionId) _current = null;
        if (_views.Count == 0) _environment = null; // browser processes exit once no view references the environment
        UpdateToolbar();
    }

    public void DisposeAll()
    {
        foreach (string id in _views.Keys.ToList()) Remove(id);
    }

    private void Configure(string actionId, CoreWebView2 core)
    {
        CoreWebView2Settings settings = core.Settings;
        // Pages get no route into Power Ops and nothing is remembered that could leak credentials.
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.AreDevToolsEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            if (!EmbeddedWebPolicy.AllowNavigation(e.Uri)) { e.Cancel = true; return; }
            // Truthful while a slow or stuck server has not answered yet (seen with a busy local dev server).
            if (_current == actionId) _status.Text = $"Waiting for {e.Uri} to respond... (is the app running? Reload or Close web view to stop)";
        };
        core.NewWindowRequested += (_, e) =>
        {
            // Pop-ups / target=_blank never spawn a second embedded browser.
            e.Handled = true;
            if (EmbeddedWebPolicy.ExternalTarget(e.Uri) is Uri target)
            {
                Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
            }
        };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.BasicAuthenticationRequested += (_, e) =>
        {
            // Supply the saved Mongoku sign-in once per navigation, only to the web app's own origin and only over
            // https or localhost. If it is rejected (or none is saved) WebView2 shows its normal sign-in prompt.
            if (!_views.TryGetValue(actionId, out var entry) || !Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? challenge)) return;
            var appUri = new Uri(entry.App.Url.Trim());
            if (ReportAuth.Origin(challenge) != ReportAuth.Origin(appUri) || ReportAuth.RefusalToSend(challenge) is not null) return;
            if (_signInAttempts.GetValueOrDefault(actionId) >= 1) return;
            if (_signInFor(appUri) is not BasicCredential saved) return;
            _signInAttempts[actionId] = _signInAttempts.GetValueOrDefault(actionId) + 1;
            e.Response.UserName = saved.UserName;
            e.Response.Password = saved.Password;
        };
        core.NavigationCompleted += (_, e) => { if (e.IsSuccess) _signInAttempts.Remove(actionId); };
        core.DocumentTitleChanged += (_, _) => { if (_current == actionId) UpdateToolbar(); };
        core.SourceChanged += (_, _) => { if (_current == actionId) UpdateToolbar(); };
        core.NavigationCompleted += (_, e) =>
        {
            if (_current != actionId) return;
            _status.Text = e.IsSuccess
                ? $"Embedded view · {_views.Count} open · separate profile in the Power Ops data folder · pop-ups open in your browser"
                : $"Could not load the page ({e.WebErrorStatus}). Is the app running? Reload when it is.";
        };
        core.ProcessFailed += (_, _) => { if (_current == actionId) _status.Text = "The page stopped unexpectedly. Reload to try again."; };
    }

    private CoreWebView2? Current() =>
        _current is not null && _views.TryGetValue(_current, out var entry) ? entry.View.CoreWebView2 : null;

    private void UpdateToolbar()
    {
        if (_current is null || !_views.TryGetValue(_current, out var entry))
        {
            _title.Text = string.Empty;
            _address.Text = string.Empty;
            return;
        }

        CoreWebView2? core = entry.View.CoreWebView2;
        _title.Text = entry.App.Name + (string.IsNullOrWhiteSpace(core?.DocumentTitle) ? string.Empty : " · " + core!.DocumentTitle);
        _address.Text = core?.Source ?? entry.App.Url;
    }

    private static Button ToolbarButton(string glyph, string name, Action action)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") },
            ToolTip = name,
            Margin = new Thickness(2),
            Padding = new Thickness(8, 4, 8, 4),
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }
}
