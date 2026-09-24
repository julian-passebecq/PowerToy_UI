using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JUtility.App.Services;
using JUtility.Core.Actions;

namespace JUtility.App;

// Transient radial menu at the pointer. Presentation only: slots raise ActionInvoked and MainWindow runs
// them through the shared dispatcher. Hidden = no work at all; the window is kept for fast re-show.
internal sealed class QuickRingWindow : Window
{
    private const double Size = 320, Radius = 112, SlotSize = 56, CenterSize = 72;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly Button _center;
    private readonly TextBlock _caption;
    private readonly List<(QuickSurfaceItem Item, Button Button)> _slots = [];
    private IntPtr _previousForeground;
    private bool _closingForAction;

    public QuickRingWindow()
    {
        Title = "Power Ops Quick Ring";
        AutomationProperties.SetName(this, "Power Ops Quick Ring");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = Size;
        Height = Size;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Only the disc is hit-testable; clicks outside it fall through, activate the other window and dismiss the ring.
        var disc = new Ellipse
        {
            Width = Size - 16,
            Height = Size - 16,
            Fill = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE8)),
            StrokeThickness = 1,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.25 },
        };
        Canvas.SetLeft(disc, 8);
        Canvas.SetTop(disc, 8);
        _canvas.Children.Add(disc);

        _caption = new TextBlock
        {
            Width = 150,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x65, 0x77)),
            FontSize = 11,
            Text = "Power Ops",
        };
        Canvas.SetLeft(_caption, Size / 2 - 75);
        Canvas.SetTop(_caption, Size / 2 + CenterSize / 2 + 2);
        _canvas.Children.Add(_caption);

        _center = CreateButton(CenterSize, "", 26);
        _center.ToolTip = "Open Power Ops";
        AutomationProperties.SetName(_center, "Open Power Ops");
        _center.Click += (_, _) => Invoke(null);
        _center.GotKeyboardFocus += (_, _) => _caption.Text = "Open Power Ops";
        _center.MouseEnter += (_, _) => _caption.Text = "Open Power Ops";
        Place(_center, 0, 0, CenterSize);



        KeyboardNavigation.SetTabNavigation(_canvas, KeyboardNavigationMode.Cycle);
        Content = _canvas;

        PreviewKeyDown += Ring_KeyDown;
        Deactivated += (_, _) =>
        {
            // Outside click / Alt+Tab: the user already moved on, so do not pull focus back.
            if (IsVisible && !_closingForAction) Hide();
        };
        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, GwlExStyle, new IntPtr(GetWindowLongPtr(handle, GwlExStyle).ToInt64() | WsExToolWindow));
        };
    }

    /// <summary>Raised after the ring is hidden and focus is back on the previous window; null = centre (open Power Ops).</summary>
    public event EventHandler<string?>? ActionInvoked;

    public IReadOnlyList<string> SlotIds => _slots.Select(x => x.Item.Id).ToList();

    public void ShowAtPointer(IReadOnlyList<QuickSurfaceItem> items)
    {
        Render(items);
        IntPtr self = new WindowInteropHelper(this).EnsureHandle();
        IntPtr previous = GetForegroundWindow();
        _previousForeground = previous == self ? IntPtr.Zero : previous;
        WindowPlacementService.CenterOnCursor(this);
        _caption.Text = "Power Ops";
        Show();
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => _center.Focus()));
    }

    /// <summary>Esc / toggle: hide and give focus back to the window that was active before the ring.</summary>
    public void Dismiss()
    {
        if (!IsVisible) return;
        _closingForAction = true;
        try
        {
            Hide();
            RestorePreviousForeground();
        }
        finally
        {
            _closingForAction = false;
        }
    }

    private void Render(IReadOnlyList<QuickSurfaceItem> items)
    {
        foreach ((_, Button button) in _slots) _canvas.Children.Remove(button);
        _slots.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            QuickSurfaceItem item = items[i];
            Button button = CreateButton(SlotSize, item.GlyphText, 20);
            button.ToolTip = $"{i + 1}. {item.ToolTip}";
            button.Opacity = item.IsAvailable ? 1.0 : 0.45;
            AutomationProperties.SetName(button, item.Label);
            AutomationProperties.SetHelpText(button, item.UnavailableReason ?? string.Empty);
            AutomationProperties.SetAcceleratorKey(button, (i + 1).ToString());
            string caption = item.IsAvailable ? $"{i + 1}  {item.Label}" : $"{i + 1}  {item.Label} (unavailable)";
            button.GotKeyboardFocus += (_, _) => _caption.Text = caption;
            button.MouseEnter += (_, _) => _caption.Text = caption;
            string id = item.Id;
            button.Click += (_, _) => Invoke(id);
            (double x, double y) = QuickRingModel.SlotOffset(i, items.Count, Radius);
            Place(button, x, y, SlotSize);
            _slots.Add((item, button));
        }
    }

    private void Invoke(string? actionId)
    {
        _closingForAction = true;
        try
        {
            Hide();
            RestorePreviousForeground();
        }
        finally
        {
            _closingForAction = false;
        }

        ActionInvoked?.Invoke(this, actionId);
    }

    private void Ring_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        int current = _slots.FindIndex(x => x.Button.IsKeyboardFocused);
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Dismiss();
                return;
            case Key.Left or Key.Up:
                e.Handled = true;
                FocusSlot(QuickRingModel.Move(current, _slots.Count, -1));
                return;
            case Key.Right or Key.Down:
                e.Handled = true;
                FocusSlot(QuickRingModel.Move(current, _slots.Count, 1));
                return;
        }

        int digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D0
            : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad0
            : 0;
        if (QuickRingModel.SlotForDigit(digit, _slots.Count) is int slot)
        {
            e.Handled = true;
            Invoke(_slots[slot].Item.Id);
        }
    }

    private void FocusSlot(int index)
    {
        if (index >= 0 && index < _slots.Count) _slots[index].Button.Focus();
    }

    private void RestorePreviousForeground()
    {
        if (_previousForeground != IntPtr.Zero && IsWindow(_previousForeground)) SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    private void Place(Button button, double offsetX, double offsetY, double size)
    {
        Canvas.SetLeft(button, Size / 2 + offsetX - size / 2);
        Canvas.SetTop(button, Size / 2 + offsetY - size / 2);
        _canvas.Children.Add(button);
    }

    private static Button CreateButton(double size, string glyph, double fontSize) => new()
    {
        Width = size,
        Height = size,
        MinHeight = 0,
        Padding = new Thickness(0),
        Margin = new Thickness(0),
        Background = Brushes.White,
        Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = fontSize },
        Template = RoundTemplate(size / 2),
    };

    private static ControlTemplate RoundTemplate(double radius) => (ControlTemplate)XamlReader.Parse($$"""
        <ControlTemplate TargetType="Button"
                         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Border x:Name="Chrome" CornerRadius="{{radius}}" Background="{TemplateBinding Background}" BorderBrush="#D8DEE8" BorderThickness="1">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Chrome" Property="Background" Value="#E6F0FA"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Chrome" Property="Background" Value="#CFE2F5"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Chrome" Property="BorderBrush" Value="#0F6CBD"/>
              <Setter TargetName="Chrome" Property="BorderThickness" Value="2"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

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
