using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PowerRing.Core;

namespace PowerRing;

// The ring itself: a transparent, top-most window at the pointer. Pure presentation over RingNavigator; actions are
// raised after the ring is hidden and focus is back on the window the user was in (so "keys" reach that window).
internal sealed class RingWindow : Window
{
    private const double PillsHeight = 40, Pad = 18;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly Canvas _root = new();
    private readonly Canvas _slots = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ScaleTransform _slotScale = new(1, 1);
    private RingConfig _config;
    private readonly RingNavigator _nav;
    private RingTheme _theme = null!;
    private readonly List<(RingItem Item, Button Button)> _buttons = [];
    private Button _center = null!;
    private TextBlock _caption = null!;
    private IntPtr _previous;
    private bool _busy;

    public RingWindow(RingConfig config)
    {
        _config = config;
        _nav = new RingNavigator(config);
        Title = "Power Ring";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        _root.RenderTransform = _scale;
        _slots.RenderTransform = _slotScale;
        Content = _root;
        KeyboardNavigation.SetTabNavigation(_root, KeyboardNavigationMode.None);

        PreviewKeyDown += OnKey;
        PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; GoBack(); };
        PreviewMouseWheel += (_, e) => { e.Handled = true; SwitchProfile(_nav.ProfileIndex + (e.Delta < 0 ? 1 : -1)); };
        Deactivated += (_, _) => { if (IsVisible && !_busy) Dismiss(restoreFocus: false); };
        SourceInitialized += (_, _) => Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WsExToolWindow);
    }

    /// <summary>Raised after the ring is hidden and focus restored. Null = centre of the first circle (Power Ops).</summary>
    public event EventHandler<RingItem?>? Invoked;
    public event EventHandler<int>? ProfileChanged;

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
        if (IsVisible) Render(animate: true);
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
        Animate(_root, _scale, 0.9);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _center.Focus());
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
        // Centre the DISC (not the whole window, which has the profile buttons on top) on the pointer.
        double discCenterY = (PillsVisible ? PillsHeight : 0) + Pad + _config.Appearance.RingSize / 2;
        int x = pointer.X - w / 2, y = pointer.Y - (int)Math.Round(discCenterY * scale);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - h));
        Native.SetWindowPos(handle, new IntPtr(-1), x, y, w, h, 0x0010);
    }

    private bool PillsVisible => _nav.Profiles.Count > 1;

    private void Render(bool animate)
    {
        RingAppearance a = _config.Appearance;
        _theme = RingTheme.Resolve(a, _nav.Profile);
        _root.Children.Clear();
        _slots.Children.Clear();
        _buttons.Clear();

        double ring = a.RingSize, top = (PillsVisible ? PillsHeight : 0) + Pad;
        Width = ring + 2 * Pad;
        Height = top + ring + Pad;
        _root.Width = Width;
        _root.Height = Height;
        _scale.CenterX = Width / 2;
        _scale.CenterY = top + ring / 2;
        double cx = Width / 2, cy = top + ring / 2;
        _slotScale.CenterX = cx;
        _slotScale.CenterY = cy;

        if (PillsVisible) RenderPills();

        var disc = new Ellipse
        {
            Width = ring,
            Height = ring,
            Fill = RingTheme.Brush(_theme.Background),
            Stroke = RingTheme.Brush(_theme.Border),
            StrokeThickness = 1,
            Effect = a.Shadow ? new DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Opacity = _theme.Dark ? 0.55 : 0.25, Direction = 270 } : null,
        };
        Canvas.SetLeft(disc, Pad);
        Canvas.SetTop(disc, top);
        _root.Children.Add(disc);
        _root.Children.Add(_slots);

        if (!_nav.AtRoot)
        {
            var crumb = new TextBlock
            {
                Text = _nav.Breadcrumb,
                Width = ring * 0.6,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = a.FontSize - 1,
                Foreground = RingTheme.Brush(_theme.Muted),
            };
            Canvas.SetLeft(crumb, cx - ring * 0.3);
            Canvas.SetTop(crumb, top + 10);
            _slots.Children.Add(crumb);
        }

        double radius = a.SlotRadius ?? RingNavigator.DefaultSlotRadius(a);
        IReadOnlyList<RingItem> items = _nav.Items;
        for (int i = 0; i < items.Count; i++)
        {
            RingItem item = items[i];
            (double dx, double dy) = RingNavigator.SlotOffset(i, items.Count, radius);
            Button button = SlotButton(item, i, a);
            Place(button, cx + dx, cy + dy, a.SlotSize);
            _slots.Children.Add(button);
            _buttons.Add((item, button));
            if (a.ShowNumbers)
            {
                double factor = (radius + a.SlotSize / 2 + 9) / radius;
                var number = new TextBlock { Text = (i + 1).ToString(), FontSize = a.FontSize - 2, Foreground = RingTheme.Brush(_theme.Muted), IsHitTestVisible = false };
                number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(number, cx + dx * factor - number.DesiredSize.Width / 2);
                Canvas.SetTop(number, cy + dy * factor - number.DesiredSize.Height / 2);
                if (factor * radius < ring / 2 - 4) _slots.Children.Add(number);
            }
        }

        string centerName = _nav.AtRoot ? "Open Power Ops" : "Back";
        _center = RoundButton(a.CenterSize, _nav.AtRoot ? Glyph(_nav.Profile.Icon ?? "powerops", a.IconSize + 4) : Glyph("back", a.IconSize + 2), _theme.Slot, _theme.Accent);
        AutomationProperties.SetName(_center, centerName);
        _center.ToolTip = centerName;
        _center.Click += (_, _) => { if (_nav.AtRoot) Fire(null); else GoBack(); };
        _center.MouseEnter += (_, _) => Caption(centerName);
        _center.GotKeyboardFocus += (_, _) => Caption(centerName);
        Place(_center, cx, cy, a.CenterSize);
        _slots.Children.Add(_center);

        _caption = new TextBlock
        {
            Width = ring * 0.5,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = a.FontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = RingTheme.Brush(_theme.Text),
            IsHitTestVisible = false,
            Visibility = a.ShowLabels ? Visibility.Visible : Visibility.Collapsed,
        };
        Canvas.SetLeft(_caption, cx - ring * 0.25);
        Canvas.SetTop(_caption, cy + a.CenterSize / 2 + 4);
        _slots.Children.Add(_caption);
        Caption(_nav.AtRoot ? _nav.Profile.Name : _nav.Items.Count + " actions");

        AutomationProperties.SetName(this, _nav.AtRoot ? $"Power Ring - {_nav.Profile.Name}" : $"Power Ring - {_nav.Breadcrumb}");
        if (animate) Animate(_slots, _slotScale, 0.94);
    }

    private void RenderPills()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < _nav.Profiles.Count; i++)
        {
            RingProfile profile = _nav.Profiles[i];
            bool active = i == _nav.ProfileIndex;
            Color accent = RingTheme.Parse(profile.Accent) ?? _theme.Accent;
            Color fill = active ? accent : _theme.Slot;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0), Opacity = 0.75 });
            if (RingIcons.Glyph(profile.Icon) is string glyph)
                content.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 13, Margin = new Thickness(0, 1, 5, 0) });
            content.Children.Add(new TextBlock { Text = profile.Name });
            var pill = new Button
            {
                Content = content,
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(3, 0, 3, 0),
                FontSize = _config.Appearance.FontSize,
                Foreground = RingTheme.Brush(active ? RingTheme.OnColor(fill) : _theme.Text),
                Background = RingTheme.Brush(fill),
                Focusable = false,
                Template = MakeTemplate(14, fill, active ? fill : Blend(fill, accent, 0.35), _theme.Border),
            };
            AutomationProperties.SetName(pill, $"Profile {i + 1}: {profile.Name}" + (active ? " (active)" : ""));
            int index = i;
            pill.Click += (_, _) => SwitchProfile(index);
            row.Children.Add(pill);
        }
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(row, Math.Max(0, (Width - row.DesiredSize.Width) / 2));
        Canvas.SetTop(row, 6);
        _root.Children.Add(row);
    }

    private Button SlotButton(RingItem item, int index, RingAppearance a)
    {
        Color fill = RingTheme.Parse(item.Color) ?? _theme.Slot;
        var face = new Grid();
        face.Children.Add(ItemIcon(item, a.IconSize));
        if (item.IsGroup)
        {
            // Small chevron badge: this button opens another circle.
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = RingTheme.Brush(_theme.Accent),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -2, -2),
                Child = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 10, Foreground = RingTheme.Brush(RingTheme.OnColor(_theme.Accent)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            face.Children.Add(badge);
        }
        Button button = RoundButton(a.SlotSize, face, fill, _theme.SlotHover);
        button.Foreground = RingTheme.Brush(RingTheme.Parse(item.Color) is { } own ? RingTheme.OnColor(own) : _theme.Icon);
        string name = item.IsGroup ? item.Label + " ›" : item.Label;
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAcceleratorKey(button, (index + 1).ToString());
        button.ToolTip = $"{index + 1}. {name}";
        button.MouseEnter += (_, _) => Caption(name);
        button.GotKeyboardFocus += (_, _) => Caption(name);
        button.Click += (_, _) => Choose(item);
        return button;
    }

    private void Choose(RingItem item)
    {
        if (item.IsGroup)
        {
            _nav.Open(item);
            Render(animate: true);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (_buttons.Count > 0) _buttons[0].Button.Focus(); });
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
        ProfileChanged?.Invoke(this, _nav.ProfileIndex);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _center.Focus());
    }

    private void Fire(RingItem? item)
    {
        Dismiss(restoreFocus: true);
        Invoked?.Invoke(this, item);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        ModifierKeys mods = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int focused = _buttons.FindIndex(x => x.Button.IsKeyboardFocused);
        e.Handled = true;
        switch (key)
        {
            case Key.Escape or Key.Back: GoBack(); return;
            case Key.Tab: SwitchProfile(_nav.ProfileIndex + (mods.HasFlag(ModifierKeys.Shift) ? -1 : 1)); return;
            case >= Key.F1 and <= Key.F5: SwitchProfile(key - Key.F1); return;
            case Key.Right or Key.Down: Focus(focused < 0 ? 0 : (focused + 1) % _buttons.Count); return;
            case Key.Left or Key.Up: Focus(focused < 0 ? _buttons.Count - 1 : (focused - 1 + _buttons.Count) % _buttons.Count); return;
            case Key.Enter or Key.Space:
                if (focused >= 0) Choose(_buttons[focused].Item);
                else if (_center.IsKeyboardFocused) { if (_nav.AtRoot) Fire(null); else GoBack(); }
                return;
        }
        int digit = key is >= Key.D1 and <= Key.D9 ? key - Key.D0 : key is >= Key.NumPad1 and <= Key.NumPad9 ? key - Key.NumPad0 : 0;
        if (digit > 0 && mods.HasFlag(ModifierKeys.Control)) { SwitchProfile(digit - 1); return; }
        if (digit > 0 && digit <= _buttons.Count) { Choose(_buttons[digit - 1].Item); return; }
        e.Handled = false;
    }

    private void Focus(int index)
    {
        if (index >= 0 && index < _buttons.Count) _buttons[index].Button.Focus();
    }

    private void Caption(string text) => _caption.Text = text;

    private void RestoreFocus()
    {
        if (_previous != IntPtr.Zero && Native.IsWindow(_previous)) Native.SetForegroundWindow(_previous);
        _previous = IntPtr.Zero;
    }

    private void Animate(UIElement element, ScaleTransform scale, double from)
    {
        int ms = _config.Appearance.AnimationMs;
        if (ms <= 0)
        {
            element.Opacity = 1;
            if (element == _root) Opacity = 1;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (element == _root) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        else element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, 1, duration) { EasingFunction = ease });
    }

    private static UIElement ItemIcon(RingItem item, double size)
    {
        string icon = item.Icon ?? RingIcons.Default(item);
        if (RingIcons.IsFile(icon) && IconLoader.Load(Environment.ExpandEnvironmentVariables(icon)) is ImageSource image)
            return new Image { Source = image, Width = size + 4, Height = size + 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new TextBlock
        {
            Text = RingIcons.Glyph(icon) ?? RingIcons.Glyph(RingIcons.Default(item))!,
            FontFamily = IconFont,
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static TextBlock Glyph(string name, double size) => new()
    {
        Text = RingIcons.Glyph(name) ?? RingIcons.Glyph("powerops")!,
        FontFamily = IconFont,
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Button RoundButton(double size, UIElement content, Color fill, Color hover) => new()
    {
        Width = size,
        Height = size,
        Content = content,
        Template = MakeTemplate(size / 2, fill, hover, _theme.Border),
        Foreground = RingTheme.Brush(_theme.Icon),
        FocusVisualStyle = null,
        Cursor = Cursors.Hand,
    };

    private static void Place(FrameworkElement element, double centerX, double centerY, double size)
    {
        Canvas.SetLeft(element, centerX - size / 2);
        Canvas.SetTop(element, centerY - size / 2);
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Round button template built in code (no XAML parser loaded: keeps the ring light).</summary>
    private ControlTemplate MakeTemplate(double radius, Color fill, Color hover, Color border)
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        chrome.SetValue(Border.BackgroundProperty, RingTheme.Brush(fill));
        chrome.SetValue(Border.BorderBrushProperty, RingTheme.Brush(border));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        chrome.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(ForegroundProperty));
        var face = new FrameworkElementFactory(typeof(ContentPresenter), "Face");
        face.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        face.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        face.SetValue(RenderTransformOriginProperty, new Point(0.5, 0.5));
        chrome.AppendChild(face);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Border.BackgroundProperty, RingTheme.Brush(hover), "Chrome"));
        over.Setters.Add(new Setter(TextElement.ForegroundProperty, RingTheme.Brush(RingTheme.OnColor(hover)), "Chrome"));
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, RingTheme.Brush(_theme.Accent), "Chrome"));
        focused.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(RenderTransformProperty, new ScaleTransform(0.9, 0.9), "Face"));
        template.Triggers.Add(over);
        template.Triggers.Add(focused);
        template.Triggers.Add(pressed);
        template.Seal();
        return template;
    }
}

/// <summary>Image icons from ring.json: .png/.ico/.jpg files, or the icon of an .exe. Cached; a missing file falls back to the glyph.</summary>
internal static class IconLoader
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string path)
    {
        if (Cache.TryGetValue(path, out ImageSource? cached)) return cached;
        ImageSource? image = null;
        try
        {
            if (File.Exists(path))
            {
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon is not null)
                        image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
                else
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path);
                    bitmap.DecodePixelWidth = 64;
                    bitmap.EndInit();
                    image = bitmap;
                }
                image?.Freeze();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        Cache[path] = image;
        return image;
    }
}
