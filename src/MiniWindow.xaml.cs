using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace RadioPlayer;

/// <summary>A one-line strip that stays on top of other windows: station, play/pause, volume.</summary>
public partial class MiniWindow : Window
{
    private readonly App _app;
    private readonly Radio _radio;

    public MiniWindow(App app, Settings settings)
    {
        _app = app;
        _radio = app.Radio;
        InitializeComponent();
        DataContext = _radio;

        // Where it was last time, or the bottom-right corner just above the taskbar.
        var area = SystemParameters.WorkArea;
        (Left, Top) = settings.MiniLeft is double left && settings.MiniTop is double top
                      && Native.IsOnScreen(left, top, Width, Height)
            ? (left, top)
            : (area.Right - Width - 16, area.Bottom - Height - 16);

        MouseLeftButtonDown += OnLeftButtonDown;
        MouseRightButtonUp += OnRightButtonUp;
        PreviewKeyDown += (_, e) => e.Handled = Shortcuts.Handle(_radio, e.Key);
        PreviewMouseWheel += (_, e) => { _radio.Volume += e.Delta > 0 ? 5 : -5; e.Handled = true; };

        // Alt+F4 hides it like the main window's close button does.
        Closing += (_, e) =>
        {
            if (_app.IsExiting) return;
            e.Cancel = true;
            _app.HideToTray();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.RoundCorners(this);
    }

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) _app.ShowMain();
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = AppMenu.Build(_app);
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _radio.Toggle();

    private void Mute_Click(object sender, RoutedEventArgs e) => _radio.IsMuted = !_radio.IsMuted;

    private void Expand_Click(object sender, RoutedEventArgs e) => _app.ShowMain();

    private void Hide_Click(object sender, RoutedEventArgs e) => _app.HideToTray();
}
