using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.Core.Actions;

namespace JUtility.App;

// Presentation only: every button raises ActionInvoked and MainWindow runs it through the shared
// dispatcher. No timers run while the Shelf is idle; the collapse timer is one-shot after pointer leave.
internal sealed class QuickShelfWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly StackPanel _layout = new();
    private readonly StackPanel _items = new();
    private readonly Button _grip;
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly List<(QuickSurfaceItem Item, Button Button)> _buttons = [];
    private bool _autoHide;
    private bool _keyboardMode;
    private IntPtr _previousForeground;

    public QuickShelfWindow()
    {
        Title = "Power Ops Quick Shelf";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE8));
        BorderThickness = new Thickness(1);
        AutomationProperties.SetName(this, "Power Ops Quick Shelf");

        _grip = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 14 },
            Width = 26,
            Height = 40,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to move. Click for Quick Shelf options. Esc hides the Shelf.",
        };
        AutomationProperties.SetName(_grip, "Quick Shelf options");
        _grip.PreviewMouseLeftButtonDown += Grip_MouseDown;
        _grip.Click += (_, _) => OpenMenu();

        _layout.Children.Add(_grip);
        _layout.Children.Add(_items);
        KeyboardNavigation.SetTabNavigation(_layout, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(_layout, KeyboardNavigationMode.Cycle);
        Content = _layout;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                HideRestoringFocus();
            }
        };
        MouseEnter += (_, _) =>
        {
            _collapseTimer.Stop();
            SetCollapsed(false);
            Hovered?.Invoke(this, EventArgs.Empty);
        };
        MouseLeave += (_, _) => { if (_autoHide) _collapseTimer.Start(); };
        LostKeyboardFocus += (_, _) => { if (_autoHide && !IsMouseOver) _collapseTimer.Start(); };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOver && !IsKeyboardFocusWithin && _grip.ContextMenu?.IsOpen != true) SetCollapsed(true);
        };
        SourceInitialized += (_, _) =>
        {
            // Tool window: not in Alt+Tab. No-activate: clicking a button does not steal focus from the user's app.
            IntPtr handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExToolWindow | WsExNoActivate));
        };
        Deactivated += (_, _) => SetKeyboardMode(false);
        Closed += (_, _) => _collapseTimer.Stop();
        SetKeyboardMode(false);
    }

    public event EventHandler<string>? ActionInvoked;
    public event EventHandler? Hovered;
    public event EventHandler? MoveCompleted;

    public bool IsCollapsed => _items.Visibility != Visibility.Visible;

    public void Render(IReadOnlyList<QuickSurfaceItem> items, ShelfOrientation orientation, bool topmost, bool autoHide)
    {
        Orientation direction = orientation == ShelfOrientation.Vertical ? Orientation.Vertical : Orientation.Horizontal;
        _layout.Orientation = direction;
        _items.Orientation = direction;
        _grip.Width = direction == Orientation.Horizontal ? 26 : 40;
        _grip.Height = direction == Orientation.Horizontal ? 40 : 26;
        Topmost = topmost;
        _autoHide = autoHide;
        if (!autoHide)
        {
            _collapseTimer.Stop();
            SetCollapsed(false);
        }

        if (_buttons.Select(x => x.Item.Id).SequenceEqual(items.Select(x => x.Id)))
        {
            // Same layout: refresh availability in place so the button under the pointer is not replaced.
            for (int i = 0; i < items.Count; i++)
            {
                Apply(_buttons[i].Button, items[i]);
                _buttons[i] = (items[i], _buttons[i].Button);
            }

            return;
        }

        _items.Children.Clear();
        _buttons.Clear();
        foreach (QuickSurfaceItem item in items)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = item.GlyphText, FontFamily = IconFont, FontSize = 18 },
                Width = 40,
                Height = 40,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = _keyboardMode,
            };
            ToolTipService.SetInitialShowDelay(button, 250);
            string id = item.Id;
            button.Click += (_, _) => ActionInvoked?.Invoke(this, id);
            Apply(button, item);
            _buttons.Add((item, button));
            _items.Children.Add(button);
        }
    }

    public void AttachMenu(ContextMenu menu)
    {
        menu.PlacementTarget = _grip;
        _grip.ContextMenu = menu;
    }

    /// <summary>Shows without taking focus (startup / pointer use).</summary>
    public void ShowPassive()
    {
        if (!IsActive) SetKeyboardMode(false);
        if (!IsVisible) Show();
        SetCollapsed(false);
        if (_autoHide && !IsMouseOver) _collapseTimer.Start();
    }

    /// <summary>Shows and takes keyboard focus; Esc later returns focus to the previous application.</summary>
    public void ShowForKeyboard()
    {
        IntPtr previous = GetForegroundWindow();
        if (previous != new WindowInteropHelper(this).Handle) _previousForeground = previous;
        ShowPassive();
        SetKeyboardMode(true);
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            (_buttons.Count > 0 ? _buttons[0].Button : _grip).Focus();
        }));
    }

    public void HideRestoringFocus()
    {
        bool hadFocus = IsActive;
        Hide();
        if (hadFocus && _previousForeground != IntPtr.Zero && IsWindow(_previousForeground)) SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    public void EnsureOnScreen() => WindowPlacementService.EnsureVisible(this);

    private static void Apply(Button button, QuickSurfaceItem item)
    {
        button.ToolTip = item.ToolTip;
        button.Opacity = item.IsAvailable ? 1.0 : 0.45;
        AutomationProperties.SetName(button, item.Label);
        AutomationProperties.SetHelpText(button, item.UnavailableReason ?? string.Empty);
    }

    // A focusable WPF button takes keyboard focus on mouse down, and SetFocus activates the Shelf even with
    // WS_EX_NOACTIVATE when Power Ops owns the foreground. Buttons are focusable only in keyboard mode.
    private void SetKeyboardMode(bool enabled)
    {
        _keyboardMode = enabled;
        _grip.Focusable = enabled;
        foreach ((_, Button button) in _buttons) button.Focusable = enabled;
    }

    private void SetCollapsed(bool collapsed) =>
        _items.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

    private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        double left = Left, top = Top;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (Math.Abs(Left - left) < 1 && Math.Abs(Top - top) < 1)
        {
            OpenMenu();
            return;
        }

        EnsureOnScreen();
        MoveCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OpenMenu()
    {
        if (_grip.ContextMenu is { } menu)
        {
            menu.IsOpen = true;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
}
