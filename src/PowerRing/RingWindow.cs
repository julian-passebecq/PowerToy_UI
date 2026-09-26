using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PowerRing.Core;

namespace PowerRing;

/// <summary>What a "board" workspace shows: recent copies (memory only) and quick notes. Provided by the host.</summary>
internal interface IBoardSource
{
    IReadOnlyList<ClipText> Texts { get; }
    IReadOnlyList<ClipImage> Images { get; }
    RingNotesStore Notes { get; }
    void CopyText(string text);
    void CopyImage(ClipImage image);
}

internal sealed record ClipImage(BitmapSource Thumbnail, byte[] Png, int Width, int Height, DateTimeOffset At);

// The ring: a transparent, top-most window at the pointer. Circle 1 = the profile's buttons, circle 2 = their children
// right behind them, circle 3 = the children's children, all visible at once. The centre shows the workspace and the
// small icons of the previous/next workspaces. A "board" workspace shows tables instead (clipboard, images, notes, links).
// Actions are raised after the ring is hidden and focus is back on the window the user was in (so "keys" reach it).
internal sealed class RingWindow : Window
{
    private const double Pad = 16;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    private readonly Canvas _root = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RingNavigator _nav;
    private readonly IBoardSource _board;
    private readonly Dictionary<string, int> _tableIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(RingItem Item, Button Button)> _mainButtons = [];
    private readonly List<Action> _cells = [];
    private RingConfig _config;
    private RingTheme _theme = null!;
    private Button _center = null!;
    private TextBlock? _caption;
    private TextBox? _noteBox;
    // Board/gallery home button: the panel is placed so that it sits under the pointer (no mouse move to go back).
    private Button? _home;
    private IntPtr _previous;
    private bool _busy;

    public RingWindow(RingConfig config, IBoardSource board)
    {
        _config = config;
        _board = board;
        _nav = new RingNavigator(config);
        Title = "Power Ring";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        FontFamily = TextFont;
        _root.RenderTransform = _scale;
        Content = _root;
        KeyboardNavigation.SetTabNavigation(_root, KeyboardNavigationMode.None);

        PreviewKeyDown += OnKey;
        PreviewMouseRightButtonUp += (_, e) => { if (!_nav.Profile.IsPanel) { e.Handled = true; GoBack(); } };
        PreviewMouseWheel += (_, e) =>
        {
            if (_nav.Profile.IsPanel && e.OriginalSource is DependencyObject source && FindParent<ScrollViewer>(source) is not null) return;
            e.Handled = true;
            SwitchProfile(_nav.ProfileIndex + (e.Delta < 0 ? 1 : -1));
        };
        Deactivated += (_, _) => { if (IsVisible && !_busy) Dismiss(restoreFocus: false); };
        WebIcons.Arrived += (_, _) => { if (IsVisible && _noteBox?.IsKeyboardFocused != true) Render(animate: false); };
        SourceInitialized += (_, _) => Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WsExToolWindow);
    }

    /// <summary>Raised after the ring is hidden and focus restored.</summary>
    public event EventHandler<RingItem>? Invoked;

    public RingNavigator Navigator => _nav;

    public void Reload(RingConfig config)
    {
        _config = config;
        _nav.Reload(config);
        if (IsVisible) Render(animate: false);
    }

    public void SetProfile(int index)
    {
        _nav.SetProfile(index);
        if (IsVisible) { Render(animate: true); Place(new WindowInteropHelper(this).Handle); }
    }

    /// <summary>Called by the host when a copy arrives while the board is open.</summary>
    public void BoardChanged()
    {
        if (IsVisible && _nav.Profile.IsBoard && _noteBox?.IsKeyboardFocused != true) Render(animate: false);
    }

    /// <summary>Draws one workspace into a PNG without showing anything (visual checks that need no free desktop).</summary>
    public void Snapshot(int profileIndex, string path)
    {
        _nav.SetProfile(profileIndex);
        Render(animate: false);
        _root.Measure(new Size(Width, Height));
        _root.Arrange(new Rect(0, 0, Width, Height));
        _root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Width * 1.5), (int)Math.Ceiling(Height * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    public void Toggle()
    {
        if (IsVisible) { Dismiss(restoreFocus: true); return; }
        ShowAtPointer();
    }

    public void ShowAtPointer()
    {
        IntPtr self = new WindowInteropHelper(this).EnsureHandle();
        IntPtr foreground = Native.GetForegroundWindow();
        _previous = foreground == self ? IntPtr.Zero : foreground;
        _nav.Home();
        Render(animate: false);
        Opacity = 0;
        Show();
        Place(self);
        Activate();
        Animate(0.9);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusDefault);
    }

    public void Dismiss(bool restoreFocus)
    {
        if (!IsVisible) return;
        _busy = true;
        try
        {
            Hide();
            if (restoreFocus) RestoreFocus();
        }
        finally { _busy = false; }
    }

    private void Place(IntPtr handle)
    {
        var (work, bounds, _, pointer) = Native.PointerMonitor();
        // Move onto the pointer's monitor first so WPF adopts that monitor's DPI, then size and centre in physical pixels.
        Native.SetWindowPos(handle, IntPtr.Zero, (bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2, 0, 0, 0x0001 | 0x0004 | 0x0010);
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Round(Width * scale), h = (int)Math.Round(Height * scale);
        // Ring: its centre under the pointer. Panel: its home button under the pointer.
        Point anchor = new(Width / 2, Height / 2);
        if (_home is not null)
        {
            _root.Measure(new Size(Width, Height));
            _root.Arrange(new Rect(0, 0, Width, Height));
            _root.UpdateLayout();
            anchor = _home.TranslatePoint(new Point(_home.ActualWidth / 2, _home.ActualHeight / 2), _root);
        }
        int x = Math.Clamp(pointer.X - (int)Math.Round(anchor.X * scale), work.Left, Math.Max(work.Left, work.Right - w));
        int y = Math.Clamp(pointer.Y - (int)Math.Round(anchor.Y * scale), work.Top, Math.Max(work.Top, work.Bottom - h));
        Native.SetWindowPos(handle, new IntPtr(-1), x, y, w, h, 0x0010);
    }

    // ---------------------------------------------------------------- rendering

    private void Render(bool animate)
    {
        _theme = RingTheme.Resolve(_config.Appearance, _nav.Profile);
        ShellIcons.WebIconsEnabled = _config.Appearance.WebIcons;
        _root.Children.Clear();
        _mainButtons.Clear();
        _cells.Clear();
        _noteBox = null;
        _caption = null;
        _home = null;
        if (_nav.Profile.IsBoard) RenderBoard();
        else if (_nav.Profile.IsGallery) RenderGallery();
        else RenderRing();
        AutomationProperties.SetName(this, _nav.AtRoot ? $"Power Ring - {_nav.Profile.Name}" : $"Power Ring - {_nav.Breadcrumb}");
        if (animate) Animate(0.96);
    }

    private void RenderRing()
    {
        RingAppearance a = _config.Appearance;
        RingLayout.Result layout = RingLayout.Compute(_nav.Items, a);
        double disc = layout.DiscSize;
        Width = Height = disc + 2 * Pad;
        _root.Width = Width;
        _root.Height = Height;
        double c = Width / 2;
        _scale.CenterX = _scale.CenterY = c;

        var fill = new RadialGradientBrush(Lighter(_theme.Background, _theme.Dark ? 0.07 : 0.0), _theme.Background) { RadiusX = 0.55, RadiusY = 0.55 };
        fill.Freeze();
        var plate = new Ellipse
        {
            Width = disc,
            Height = disc,
            Fill = fill,
            Stroke = RingTheme.Brush(_theme.Border),
            StrokeThickness = 1,
            Effect = a.Shadow ? new DropShadowEffect { BlurRadius = 30, ShadowDepth = 5, Opacity = _theme.Dark ? 0.55 : 0.28, Direction = 270 } : null,
        };
        Put(plate, c, c, disc);
        _root.Children.Add(plate);

        // Faint tracks behind each circle of buttons.
        foreach (double radius in layout.Radii)
        {
            var track = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = RingTheme.Brush(WithAlpha(_theme.Border, 0x30)), StrokeThickness = 1, IsHitTestVisible = false };
            Put(track, c, c, radius * 2);
            _root.Children.Add(track);
        }

        // Thin links from each child to its parent, so "behind" reads at a glance.
        foreach (RingNode node in layout.Nodes.Where(x => x.Parent is not null))
        {
            _root.Children.Add(new Line
            {
                X1 = c + node.Parent!.X, Y1 = c + node.Parent.Y, X2 = c + node.X, Y2 = c + node.Y,
                Stroke = RingTheme.Brush(WithAlpha(_theme.Accent, 0x60)), StrokeThickness = 1.2, IsHitTestVisible = false,
            });
        }

        if (!_nav.AtRoot)
        {
            TextBlock crumb = Text(_nav.Breadcrumb, a.FontSize * a.Scale - 1, _theme.Muted);
            crumb.Width = disc * 0.6;
            crumb.TextAlignment = TextAlignment.Center;
            crumb.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetLeft(crumb, c - disc * 0.3);
            Canvas.SetTop(crumb, Pad + 8);
            _root.Children.Add(crumb);
        }

        var main = new Dictionary<RingItem, Button>();
        foreach (RingNode node in layout.Nodes.OrderByDescending(x => x.Level))
        {
            Button button = NodeButton(node);
            Put(button, c + node.X, c + node.Y, node.Size);
            _root.Children.Add(button);
            if (node.Level == 1) main[node.Item] = button;
            if (node.Level == 1 && a.ShowNumbers)
            {
                double distance = Math.Max(1, Math.Sqrt(node.X * node.X + node.Y * node.Y));
                double factor = (distance - node.Size / 2 - 7) / distance;
                TextBlock number = Text((node.Index + 1).ToString(), a.FontSize * a.Scale - 3, _theme.Muted);
                number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(number, c + node.X * factor - number.DesiredSize.Width / 2);
                Canvas.SetTop(number, c + node.Y * factor - number.DesiredSize.Height / 2);
                _root.Children.Add(number);
            }
        }
        foreach (RingItem item in _nav.Items) if (main.TryGetValue(item, out Button? b)) _mainButtons.Add((item, b));

        RenderCenter(c, c, layout.CenterSize);
        _caption = Text("", a.FontSize * a.Scale, _theme.Text);
        _caption.FontWeight = FontWeights.SemiBold;
        _caption.Width = layout.CenterSize * 2.2;
        _caption.TextAlignment = TextAlignment.Center;
        _caption.TextTrimming = TextTrimming.CharacterEllipsis;
        _caption.Visibility = a.ShowLabels ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_caption, c - layout.CenterSize * 1.1);
        Canvas.SetTop(_caption, c + layout.CenterSize / 2 + 3);
        _root.Children.Add(_caption);
        Caption(_nav.AtRoot ? _nav.Profile.Name : _nav.Breadcrumb);
    }

    /// <summary>Centre: the workspace icon (back arrow inside a sub-circle), with the previous/next workspace icons on its sides.</summary>
    private void RenderCenter(double cx, double cy, double size)
    {
        RingAppearance a = _config.Appearance;
        string name = !_nav.AtRoot ? "Back" : BoardIndex() is int board ? $"{_nav.Profile.Name} (click: {_nav.Profiles[board].Name})" : _nav.Profile.Name;
        _center = Round(size, Glyph(_nav.AtRoot ? _nav.Profile.Icon ?? "home" : "back", a.IconSize * a.Scale + 2), _theme.Slot, _theme.Accent, name);
        _center.Click += (_, _) => { if (!_nav.AtRoot) GoBack(); else if (BoardIndex() is int board) SwitchProfile(board); else Dismiss(restoreFocus: true); };
        Put(_center, cx, cy, size);
        _root.Children.Add(_center);
        int count = _nav.Profiles.Count;
        if (count < 2) return;

        // Small switchers on the centre's sides: previous workspace on the left, next on the right.
        double mini = Math.Max(20, size * 0.36);
        foreach (int delta in new[] { -1, 1 })
        {
            int index = ((_nav.ProfileIndex + delta) % count + count) % count;
            RingProfile other = _nav.Profiles[index];
            Color tint = RingTheme.Parse(other.Accent) ?? _theme.Accent;
            Button switcher = Round(mini, Glyph(other.Icon ?? "apps", mini * 0.5), Lighter(_theme.Slot, _theme.Dark ? 0.1 : -0.05), tint, $"Workspace: {other.Name}");
            switcher.Click += (_, _) => SwitchProfile(index);
            Put(switcher, cx + delta * (size / 2 + mini * 0.05), cy, mini);
            _root.Children.Add(switcher);
        }
    }

    private Button NodeButton(RingNode node)
    {
        RingItem item = node.Item;
        Color fill = RingTheme.Parse(item.Color) ?? (node.Level == 1 ? _theme.Slot : Lighter(_theme.Slot, _theme.Dark ? 0.05 : -0.025));
        var face = new Grid();
        face.Children.Add(ItemIcon(item, node.IconSize));
        if (item.IsGroup)
        {
            // Chevron badge: this button opens its full circle.
            double badge = Math.Max(12, node.Size * 0.34);
            face.Children.Add(new Border
            {
                Width = badge,
                Height = badge,
                CornerRadius = new CornerRadius(badge / 2),
                Background = RingTheme.Brush(_theme.Accent),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -node.Size * 0.18, -node.Size * 0.18),
                Child = new TextBlock { Text = "", FontFamily = IconFont, FontSize = badge * 0.55, Foreground = RingTheme.Brush(RingTheme.OnColor(_theme.Accent)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }
        string name = item.IsGroup ? item.Label + " ›" : item.Label;
        Button button = Round(node.Size, face, fill, _theme.SlotHover, name);
        button.Foreground = RingTheme.Brush(RingTheme.Parse(item.Color) is { } own ? RingTheme.OnColor(own) : _theme.Icon);
        if (node.Level == 1) AutomationProperties.SetAcceleratorKey(button, (node.Index + 1).ToString());
        button.Click += (_, _) => Choose(item);
        return button;
    }

    // ---------------------------------------------------------------- board

    private void RenderBoard()
    {
        RingAppearance a = _config.Appearance;
        RingProfile profile = _nav.Profile;
        List<RingTable> tables = profile.Tables!;
        int index = Math.Clamp(_tableIndex.GetValueOrDefault(profile.Id), 0, tables.Count - 1);
        RingTable table = tables[index];
        double k = a.Scale, w = a.BoardWidth * k, h = a.BoardHeight * k;
        Width = w + 2 * Pad;
        Height = h + 2 * Pad;
        _root.Width = Width;
        _root.Height = Height;
        _scale.CenterX = Width / 2;
        _scale.CenterY = Height / 2;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var panel = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(18 * k),
            Background = RingTheme.Brush(_theme.Background),
            BorderBrush = RingTheme.Brush(_theme.Border),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14 * k, 10 * k, 14 * k, 10 * k),
            Effect = a.Shadow ? new DropShadowEffect { BlurRadius = 30, ShadowDepth = 5, Opacity = _theme.Dark ? 0.55 : 0.28, Direction = 270 } : null,
            Child = grid,
        };
        Canvas.SetLeft(panel, Pad);
        Canvas.SetTop(panel, Pad);
        _root.Children.Add(panel);

        // Header: ‹ table title › and one dot per table.
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8 * k) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button prev = Round(28 * k, Glyph("back", 13 * k), _theme.Slot, _theme.SlotHover, "Previous table");
        prev.Click += (_, _) => SwitchTable(-1);
        Button next = Round(28 * k, new TextBlock { Text = "", FontFamily = IconFont, FontSize = 13 * k, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, _theme.Slot, _theme.SlotHover, "Next table");
        next.Click += (_, _) => SwitchTable(1);
        var title = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        TextBlock titleText = Text(table.Title, (a.FontSize + 3) * k, _theme.Text);
        titleText.FontWeight = FontWeights.SemiBold;
        titleText.HorizontalAlignment = HorizontalAlignment.Center;
        title.Children.Add(titleText);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        for (int t = 0; t < tables.Count; t++)
            dots.Children.Add(new Ellipse { Width = 6 * k, Height = 6 * k, Margin = new Thickness(2), Fill = RingTheme.Brush(t == index ? _theme.Accent : WithAlpha(_theme.Muted, 0x70)) });
        title.Children.Add(dots);
        Grid.SetColumn(prev, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(next, 2);
        header.Children.Add(prev);
        header.Children.Add(title);
        header.Children.Add(next);
        grid.Children.Add(header);

        var cells = new UniformGrid { Columns = table.Columns, VerticalAlignment = VerticalAlignment.Top };
        var scroller = new ScrollViewer { Content = cells, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 1);
        grid.Children.Add(scroller);
        FillTable(table, cells, k);
        if (_cells.Count == 0)
        {
            string empty = table.Kind.ToLowerInvariant() switch
            {
                RingTableKinds.Clipboard => "Nothing copied yet. Copy some text (Ctrl+C) and it shows up here.",
                RingTableKinds.Images => "No image copied yet. Copy an image or take a screenshot.",
                RingTableKinds.Notes => "Type a note in the box and press Enter.",
                _ => "Empty.",
            };
            TextBlock hint = Text(empty, a.FontSize * k, _theme.Muted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(6, table.Kind.Equals(RingTableKinds.Notes, StringComparison.OrdinalIgnoreCase) ? 56 * k : 12, 6, 0);
            Grid.SetRow(hint, 1);
            grid.Children.Add(hint);
        }

        AddFooter(grid, k);
    }

    /// <summary>
    /// Footer of a panel, like the ring's centre: a home button (back to the first workspace, where the pointer already
    /// is) between the previous and next workspaces, so switching never needs a mouse move.
    /// </summary>
    private void AddFooter(Grid grid, double k)
    {
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8 * k, 0, 0) };
        int count = _nav.Profiles.Count;
        RingProfile first = _nav.Profiles[0];
        void Add(int index, double size, string name, string icon, bool strong)
        {
            RingProfile target = _nav.Profiles[index];
            Color tint = RingTheme.Parse(target.Accent) ?? _theme.Accent;
            Button button = Round(size, Glyph(icon, size * 0.45), strong ? _theme.Slot : Lighter(_theme.Slot, _theme.Dark ? 0.1 : -0.05), tint, name);
            button.Margin = new Thickness(5 * k, 0, 5 * k, 0);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Click += (_, _) => SwitchProfile(index);
            footer.Children.Add(button);
            if (strong) _home = button;
        }
        int previous = ((_nav.ProfileIndex - 1) % count + count) % count, next = (_nav.ProfileIndex + 1) % count;
        if (previous != 0) Add(previous, 30 * k, $"Workspace: {_nav.Profiles[previous].Name}", _nav.Profiles[previous].Icon ?? "apps", false);
        Add(0, 40 * k, $"Home: {first.Name}", first.Icon ?? "home", true);
        if (next != 0 && next != previous) Add(next, 30 * k, $"Workspace: {_nav.Profiles[next].Name}", _nav.Profiles[next].Icon ?? "apps", false);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);
    }

    /// <summary>The first board workspace (the clipboard), opened by the ring's centre.</summary>
    private int? BoardIndex()
    {
        for (int i = 0; i < _nav.Profiles.Count; i++) if (_nav.Profiles[i].IsBoard && i != _nav.ProfileIndex) return i;
        return null;
    }

    /// <summary>A gallery: titled sections of app tiles in rows (like an app library), all visible at once.</summary>
    private void RenderGallery()
    {
        RingAppearance a = _config.Appearance;
        List<RingSection> sections = _nav.Profile.Sections!;
        double k = a.Scale, w = a.BoardWidth * k, h = a.BoardHeight * k;
        Width = w + 2 * Pad;
        Height = h + 2 * Pad;
        _root.Width = Width;
        _root.Height = Height;
        _scale.CenterX = Width / 2;
        _scale.CenterY = Height / 2;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var panel = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(18 * k),
            Background = RingTheme.Brush(_theme.Background),
            BorderBrush = RingTheme.Brush(_theme.Border),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12 * k, 10 * k, 12 * k, 10 * k),
            Effect = a.Shadow ? new DropShadowEffect { BlurRadius = 30, ShadowDepth = 5, Opacity = _theme.Dark ? 0.55 : 0.28, Direction = 270 } : null,
            Child = grid,
        };
        Canvas.SetLeft(panel, Pad);
        Canvas.SetTop(panel, Pad);
        _root.Children.Add(panel);

        TextBlock title = Text(_nav.Profile.Name, (a.FontSize + 3) * k, _theme.Text);
        title.FontWeight = FontWeights.SemiBold;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 0, 0, 6 * k);
        grid.Children.Add(title);

        int columns = sections.Count <= 4 ? 2 : 3;
        var quadrants = new UniformGrid { Columns = columns, Rows = (sections.Count + columns - 1) / columns };
        Grid.SetRow(quadrants, 1);
        grid.Children.Add(quadrants);
        double tile = 64 * k;
        foreach (RingSection section in sections)
        {
            var box = new Border
            {
                Margin = new Thickness(4 * k),
                Padding = new Thickness(8 * k, 6 * k, 8 * k, 6 * k),
                CornerRadius = new CornerRadius(14 * k),
                Background = RingTheme.Brush(WithAlpha(_theme.Slot, 0xB0)),
                BorderBrush = RingTheme.Brush(WithAlpha(_theme.Border, 0x60)),
                BorderThickness = new Thickness(1),
            };
            var stack = new DockPanel { LastChildFill = true };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 4 * k) };
            if (RingIcons.Glyph(section.Icon) is string glyph)
                head.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 12 * k, Foreground = RingTheme.Brush(_theme.Accent), Margin = new Thickness(0, 1, 6, 0) });
            TextBlock name = Text(section.Title, (a.FontSize - 0.5) * k, _theme.Muted);
            name.FontWeight = FontWeights.SemiBold;
            head.Children.Add(name);
            DockPanel.SetDock(head, Dock.Top);
            stack.Children.Add(head);
            var tiles = new WrapPanel();
            foreach (RingItem item in section.Items)
            {
                var face = new StackPanel { Width = tile - 8 };
                face.Children.Add(new Border { Width = 34 * k, Height = 34 * k, CornerRadius = new CornerRadius(10 * k), Background = RingTheme.Brush(_theme.Slot), Child = ItemIcon(item, 22 * k), HorizontalAlignment = HorizontalAlignment.Center });
                TextBlock label = Text(item.Label, (a.FontSize - 2) * k, _theme.Text);
                label.TextAlignment = TextAlignment.Center;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.Margin = new Thickness(0, 3 * k, 0, 0);
                face.Children.Add(label);
                var button = new Button
                {
                    Content = face,
                    Width = tile,
                    Margin = new Thickness(0, 0, 0, 4 * k),
                    Padding = new Thickness(0, 4 * k, 0, 2 * k),
                    Template = MakeTemplate(12 * k, WithAlpha(_theme.Slot, 0), Blend(_theme.Slot, _theme.Accent, 0.25), WithAlpha(_theme.Border, 0), scaleOnHover: true),
                    Foreground = RingTheme.Brush(_theme.Icon),
                    FocusVisualStyle = null,
                    Cursor = Cursors.Hand,
                    ToolTip = item.Label,
                };
                AutomationProperties.SetName(button, item.Label);
                RingItem captured = item;
                button.Click += (_, _) => Fire(captured);
                if (!string.IsNullOrEmpty(item.Target) && RingActions.Of(item) == RingActions.Url)
                    button.MouseRightButtonUp += (_, e) => { e.Handled = true; _board.CopyText(captured.Target!); Dismiss(restoreFocus: true); Toast.Show("Copied: " + ClipHistory.Preview(captured.Target!, 50)); };
                tiles.Children.Add(button);
                _cells.Add(() => Fire(captured));
            }
            stack.Children.Add(new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            box.Child = stack;
            quadrants.Children.Add(box);
        }
        AddFooter(grid, k);
    }

    private void FillTable(RingTable table, UniformGrid cells, double k)
    {
        RingAppearance a = _config.Appearance;
        DateTimeOffset now = DateTimeOffset.Now;
        switch (table.Kind.ToLowerInvariant())
        {
            case RingTableKinds.Clipboard:
                foreach (ClipText clip in _board.Texts.Take(table.Keep ?? 20))
                {
                    string text = clip.Text;
                    AddCell(cells, k, TextCell(ClipHistory.Preview(text, 160), ClipHistory.Age(clip.At, now), k), "Copied text: " + ClipHistory.Preview(text, 60),
                        () => { _board.CopyText(text); Dismiss(restoreFocus: true); Toast.Show("Copied: " + ClipHistory.Preview(text, 40)); }, null);
                }
                break;
            case RingTableKinds.Images:
                foreach (ClipImage image in _board.Images.Take(table.Keep ?? 9))
                {
                    var stack = new StackPanel();
                    stack.Children.Add(new Image { Source = image.Thumbnail, Height = 78 * k, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center });
                    stack.Children.Add(Meta($"{image.Width}×{image.Height} · {ClipHistory.Age(image.At, now)}", k));
                    ClipImage captured = image;
                    AddCell(cells, k, stack, $"Copied image {image.Width}×{image.Height}",
                        () => { _board.CopyImage(captured); Dismiss(restoreFocus: true); Toast.Show($"Image copied ({captured.Width}×{captured.Height})"); }, null);
                }
                break;
            case RingTableKinds.Notes:
                _noteBox = new TextBox
                {
                    FontSize = a.FontSize * k,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(4),
                    Background = RingTheme.Brush(_theme.Slot),
                    Foreground = RingTheme.Brush(_theme.Text),
                    BorderBrush = RingTheme.Brush(_theme.Accent),
                    CaretBrush = RingTheme.Brush(_theme.Text),
                    ToolTip = "New note: type and press Enter",
                };
                AutomationProperties.SetName(_noteBox, "New note");
                _noteBox.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(_noteBox.Text)) return;
                    e.Handled = true;
                    _board.Notes.Add(_noteBox.Text, DateTimeOffset.Now);
                    Render(animate: false);
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _noteBox?.Focus());
                };
                cells.Children.Add(_noteBox);
                foreach (RingNote note in _board.Notes.Load())
                {
                    RingNote captured = note;
                    AddCell(cells, k, TextCell(ClipHistory.Preview(note.Text, 160), ClipHistory.Age(note.Created, now), k), "Note: " + ClipHistory.Preview(note.Text, 60),
                        () => { _board.CopyText(captured.Text); Dismiss(restoreFocus: true); Toast.Show("Note copied"); },
                        () => { _board.Notes.Remove(captured); Render(animate: false); });
                }
                break;
            default:
                foreach (RingItem item in table.Items ?? [])
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(new Border { Width = 26 * k, Height = 26 * k, Child = ItemIcon(item, 18 * k), Margin = new Thickness(0, 0, 8, 0) });
                    var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    TextBlock label = Text(item.Label, a.FontSize * k, _theme.Text);
                    label.FontWeight = FontWeights.SemiBold;
                    label.TextTrimming = TextTrimming.CharacterEllipsis;
                    texts.Children.Add(label);
                    texts.Children.Add(Meta(Describe(item), k));
                    row.Children.Add(texts);
                    RingItem captured = item;
                    AddCell(cells, k, row, item.Label, () => Fire(captured), null,
                        secondary: string.IsNullOrEmpty(item.Target) ? null : () => { _board.CopyText(captured.Target!); Dismiss(restoreFocus: true); Toast.Show("Copied: " + ClipHistory.Preview(captured.Target!, 50)); });
                }
                break;
        }
    }

    private static string Describe(RingItem item)
    {
        string target = item.Target ?? "";
        if (RingActions.Of(item) == RingActions.Url && Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
            return (uri.Host + uri.AbsolutePath).TrimEnd('/');
        return ClipHistory.Preview(target, 60);
    }

    private StackPanel TextCell(string text, string meta, double k)
    {
        var stack = new StackPanel();
        TextBlock body = Text(text, _config.Appearance.FontSize * k, _theme.Text);
        body.TextWrapping = TextWrapping.Wrap;
        body.MaxHeight = (_config.Appearance.FontSize * k + 5) * 3;
        body.TextTrimming = TextTrimming.CharacterEllipsis;
        stack.Children.Add(body);
        stack.Children.Add(Meta(meta, k));
        return stack;
    }

    private TextBlock Meta(string text, double k)
    {
        TextBlock meta = Text(text, (_config.Appearance.FontSize - 2) * k, _theme.Muted);
        meta.Margin = new Thickness(0, 3, 0, 0);
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        return meta;
    }

    /// <summary>A clickable card. Click = primary; right-click = secondary (copy the address); the × removes (notes).</summary>
    private void AddCell(UniformGrid cells, double k, UIElement content, string name, Action primary, Action? remove, Action? secondary = null)
    {
        var holder = new Grid();
        holder.Children.Add(content);
        var card = new Button
        {
            Content = holder,
            Margin = new Thickness(4),
            Padding = new Thickness(10 * k, 8 * k, 10 * k, 8 * k),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = MakeTemplate(10 * k, _theme.Slot, Blend(_theme.Slot, _theme.Accent, 0.22), _theme.Border, scaleOnHover: false),
            Foreground = RingTheme.Brush(_theme.Text),
            FocusVisualStyle = null,
            Cursor = Cursors.Hand,
            ToolTip = secondary is null ? name : name + "\nRight-click: copy the address",
        };
        AutomationProperties.SetName(card, name);
        card.Click += (_, _) => primary();
        if (secondary is not null) card.MouseRightButtonUp += (_, e) => { e.Handled = true; secondary(); };
        if (remove is not null)
        {
            var close = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 9 * k },
                Width = 18 * k,
                Height = 18 * k,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4 * k, -6 * k, 0),
                Template = MakeTemplate(9 * k, WithAlpha(_theme.Slot, 0), Color.FromRgb(0xDC, 0x26, 0x26), WithAlpha(_theme.Border, 0), scaleOnHover: false),
                Foreground = RingTheme.Brush(_theme.Muted),
                FocusVisualStyle = null,
                ToolTip = "Delete this note",
            };
            AutomationProperties.SetName(close, "Delete note");
            close.Click += (_, e) => { e.Handled = true; remove(); };
            holder.Children.Add(close);
        }
        cells.Children.Add(card);
        _cells.Add(primary);
    }

    private void SwitchTable(int delta)
    {
        List<RingTable> tables = _nav.Profile.Tables!;
        int current = _tableIndex.GetValueOrDefault(_nav.Profile.Id);
        _tableIndex[_nav.Profile.Id] = ((current + delta) % tables.Count + tables.Count) % tables.Count;
        Render(animate: true);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusDefault);
    }

    // ---------------------------------------------------------------- interaction

    private void Choose(RingItem item)
    {
        if (item.IsGroup)
        {
            _nav.Open(item);
            Render(animate: true);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (_mainButtons.Count > 0) _mainButtons[0].Button.Focus(); });
            return;
        }
        Fire(item);
    }

    private void GoBack()
    {
        if (!_nav.Back()) { Dismiss(restoreFocus: true); return; }
        Render(animate: true);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _center.Focus());
    }

    private void SwitchProfile(int index)
    {
        int count = _nav.Profiles.Count;
        _nav.SetProfile(((index % count) + count) % count);
        Render(animate: true);
        Place(new WindowInteropHelper(this).Handle);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusDefault);
    }

    private void Fire(RingItem item)
    {
        Dismiss(restoreFocus: true);
        Invoked?.Invoke(this, item);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        ModifierKeys mods = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool typing = _noteBox?.IsKeyboardFocused == true;
        if (key == Key.Escape)
        {
            e.Handled = true;
            if (typing && _noteBox!.Text.Length > 0) { _noteBox.Clear(); return; }
            if (_nav.Profile.IsPanel) Dismiss(restoreFocus: true); else GoBack();
            return;
        }
        if (key == Key.Tab) { e.Handled = true; SwitchProfile(_nav.ProfileIndex + (mods.HasFlag(ModifierKeys.Shift) ? -1 : 1)); return; }
        if (key is >= Key.F1 and <= Key.F6) { e.Handled = true; SwitchProfile(key - Key.F1); return; }
        int digit = key is >= Key.D1 and <= Key.D9 ? key - Key.D0 : key is >= Key.NumPad1 and <= Key.NumPad9 ? key - Key.NumPad0 : 0;
        if (digit > 0 && mods.HasFlag(ModifierKeys.Control)) { e.Handled = true; SwitchProfile(digit - 1); return; }
        if (typing) return;

        if (_nav.Profile.IsPanel)
        {
            if (_nav.Profile.IsBoard && key is Key.Left or Key.Right) { e.Handled = true; SwitchTable(key == Key.Left ? -1 : 1); return; }
            if (digit > 0 && digit <= _cells.Count) { e.Handled = true; _cells[digit - 1](); }
            return;
        }

        int focused = _mainButtons.FindIndex(x => x.Button.IsKeyboardFocused);
        switch (key)
        {
            case Key.Back: e.Handled = true; GoBack(); return;
            case Key.Right or Key.Down: e.Handled = true; FocusMain(focused < 0 ? 0 : (focused + 1) % _mainButtons.Count); return;
            case Key.Left or Key.Up: e.Handled = true; FocusMain(focused < 0 ? _mainButtons.Count - 1 : (focused - 1 + _mainButtons.Count) % _mainButtons.Count); return;
        }
        if (digit > 0 && digit <= _mainButtons.Count) { e.Handled = true; Choose(_mainButtons[digit - 1].Item); }
    }

    /// <summary>The note box on a notes table, the centre on a ring, else the window itself (keys still work).</summary>
    private void FocusDefault()
    {
        if (_noteBox is not null) _noteBox.Focus();
        else if (!_nav.Profile.IsPanel) _center.Focus();
        else Keyboard.Focus(this);
    }

    private void FocusMain(int index)
    {
        if (index >= 0 && index < _mainButtons.Count) _mainButtons[index].Button.Focus();
    }

    private void Caption(string text)
    {
        if (_caption is not null) _caption.Text = text;
    }

    private void RestoreFocus()
    {
        if (_previous != IntPtr.Zero && Native.IsWindow(_previous)) Native.SetForegroundWindow(_previous);
        _previous = IntPtr.Zero;
    }

    private void Animate(double from)
    {
        int ms = _config.Appearance.AnimationMs;
        if (ms <= 0) { Opacity = 1; return; }
        var duration = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity < 0.5 ? 0 : 0.6, 1, duration) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
    }

    // ---------------------------------------------------------------- building blocks

    private static UIElement ItemIcon(RingItem item, double size)
    {
        if (ShellIcons.For(item) is ImageSource image)
        {
            var picture = new Image { Source = image, Width = size + 6, Height = size + 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            return picture;
        }
        return new TextBlock
        {
            Text = RingIcons.Glyph(item.Icon) ?? RingIcons.Glyph(RingIcons.Default(item))!,
            FontFamily = IconFont,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static TextBlock Glyph(string name, double size) => new()
    {
        Text = RingIcons.Glyph(name) ?? RingIcons.Glyph("apps")!,
        FontFamily = IconFont,
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Text(string text, double size, Color color) => new()
    {
        Text = text,
        FontSize = Math.Max(8, size),
        FontFamily = TextFont,
        Foreground = RingTheme.Brush(color),
        IsHitTestVisible = false,
    };

    private Button Round(double size, UIElement content, Color fill, Color hover, string name)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            Content = content,
            Template = MakeTemplate(size / 2, fill, hover, _theme.Border, scaleOnHover: true),
            Foreground = RingTheme.Brush(_theme.Icon),
            FocusVisualStyle = null,
            Cursor = Cursors.Hand,
            ToolTip = name,
        };
        AutomationProperties.SetName(button, name);
        button.MouseEnter += (_, _) => Caption(name);
        button.GotKeyboardFocus += (_, _) => Caption(name);
        return button;
    }

    private static void Put(FrameworkElement element, double centerX, double centerY, double size)
    {
        Canvas.SetLeft(element, centerX - size / 2);
        Canvas.SetTop(element, centerY - size / 2);
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color Lighter(Color c, double amount) => amount >= 0
        ? Color.FromArgb(c.A, (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount))
        : Color.FromArgb(c.A, (byte)(c.R * (1 + amount)), (byte)(c.G * (1 + amount)), (byte)(c.B * (1 + amount)));

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (DependencyObject? current = child; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : null)
            if (current is T match) return match;
        return null;
    }

    /// <summary>Button template built in code (no XAML parser loaded: keeps the ring light). Hover grows it slightly.</summary>
    private ControlTemplate MakeTemplate(double radius, Color fill, Color hover, Color border, bool scaleOnHover)
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        chrome.SetValue(Border.BackgroundProperty, RingTheme.Brush(fill));
        chrome.SetValue(Border.BorderBrushProperty, RingTheme.Brush(border));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        chrome.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(ForegroundProperty));
        chrome.SetValue(RenderTransformOriginProperty, new Point(0.5, 0.5));
        var face = new FrameworkElementFactory(typeof(ContentPresenter), "Face");
        face.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
        face.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        face.SetValue(RenderTransformOriginProperty, new Point(0.5, 0.5));
        chrome.AppendChild(face);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, RingTheme.Brush(hover), "Chrome"));
        over.Setters.Add(new Setter(TextElement.ForegroundProperty, RingTheme.Brush(RingTheme.OnColor(hover)), "Chrome"));
        if (scaleOnHover) over.Setters.Add(new Setter(RenderTransformProperty, new ScaleTransform(1.08, 1.08), "Chrome"));
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, RingTheme.Brush(_theme.Accent), "Chrome"));
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(RenderTransformProperty, new ScaleTransform(0.92, 0.92), "Face"));
        template.Triggers.Add(over);
        template.Triggers.Add(focused);
        template.Triggers.Add(pressed);
        template.Seal();
        return template;
    }
}
