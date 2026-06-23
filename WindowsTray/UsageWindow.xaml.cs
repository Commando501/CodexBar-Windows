using System.Windows;
using System.Windows.Input;

namespace CodexBarTray;

/// <summary>
/// Borderless, draggable host for the <see cref="UsagePopup"/>. Replaces the
/// library-managed tray popover so the usage panel can be dragged anywhere and
/// optionally "pinned" — kept always on screen (topmost, no auto-hide) via the
/// tray menu. Position and pin state persist through <see cref="UiSettings"/>.
/// </summary>
public partial class UsageWindow : Window
{
    private readonly UiSettings _settings;
    private DateTime _lastAutoHide = DateTime.MinValue;

    public UsageWindow(UIElement content, UiSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Host.Content = content;
        Deactivated += OnDeactivated;
    }

    public bool AlwaysOnScreen
    {
        get => _settings.AlwaysOnScreen;
        set
        {
            _settings.AlwaysOnScreen = value;
            _settings.Save();
        }
    }

    /// <summary>
    /// Left-click on the tray icon: toggle the panel. When already visible it
    /// hides; otherwise it shows at the last saved spot (or near the tray). The
    /// recent-auto-hide guard swallows the click that just dismissed a
    /// non-pinned panel, so the same click can't immediately re-open it.
    /// </summary>
    public void ToggleFromTray()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if ((DateTime.UtcNow - _lastAutoHide).TotalMilliseconds < 300)
        {
            return;
        }

        ShowPanel();
    }

    public void ShowPanel()
    {
        Show();
        // SizeToContent has now produced real Actual* values; place the window.
        UpdateLayout();
        PositionWindow();
        Activate();
    }

    private void PositionWindow()
    {
        if (_settings.WindowLeft is double savedLeft && _settings.WindowTop is double savedTop)
        {
            Left = Clamp(savedLeft,
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
            Top = Clamp(savedTop,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
        }
        else
        {
            // First open: tuck into the bottom-right corner, near the tray.
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 12;
            Top = work.Bottom - ActualHeight - 12;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Pinned panels stay put; unpinned ones dismiss like a normal popover.
        if (!_settings.AlwaysOnScreen && IsVisible)
        {
            _lastAutoHide = DateTime.UtcNow;
            Hide();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try { DragMove(); }
        catch { /* DragMove throws if the button was released mid-call */ }

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    private static double Clamp(double value, double min, double max)
        => max < min ? min : Math.Min(Math.Max(value, min), max);
}
