using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RadioPlayer;

/// <summary>The full window: station list, play/pause and volume.</summary>
public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly Radio _radio;

    public MainWindow(App app, Settings settings)
    {
        _app = app;
        _radio = app.Radio;
        InitializeComponent();
        DataContext = _radio;

        if (settings.Left is double left && settings.Top is double top && Native.IsOnScreen(left, top, Width, 120))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        PreviewKeyDown += (_, e) => e.Handled = Shortcuts.Handle(_radio, e.Key);
        PreviewMouseWheel += OnMouseWheel;

        // The close button only hides the window; the radio keeps playing from the tray.
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
        Native.UseDarkTitleBar(this);
    }

    private void Station_Click(object sender, RoutedEventArgs e)
    {
        if (StationOf(sender) is not Station station) return;

        // Clicking the station that is already on pauses it; anything else starts it live.
        if (station == _radio.Current && _radio.WantPlaying) _radio.Pause();
        else _radio.PlayLive(station);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _radio.Toggle();

    private void Mute_Click(object sender, RoutedEventArgs e) => _radio.IsMuted = !_radio.IsMuted;

    private void Mini_Click(object sender, RoutedEventArgs e) => _app.ShowMini();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (new AddStationWindow(_radio) { Owner = this }.ShowDialog() == true) StationScroll.ScrollToEnd();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (StationOf(sender) is Station station) _radio.Move(station, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (StationOf(sender) is Station station) _radio.Move(station, +1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (StationOf(sender) is Station station) _radio.Remove(station);
    }

    /// <summary>The station a row button, or an item of its right-click menu, belongs to.</summary>
    private static Station? StationOf(object sender) => sender switch
    {
        MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement row } } => row.DataContext as Station,
        FrameworkElement element => element.DataContext as Station,
        _ => null,
    };

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Over a station list long enough to scroll, the wheel scrolls it; anywhere else it is the volume.
        if (StationScroll.ScrollableHeight > 0 && StationScroll.IsMouseOver) return;

        _radio.Volume += e.Delta > 0 ? 5 : -5;
        e.Handled = true;
    }
}
