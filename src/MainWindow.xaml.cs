using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using WinPlayer = Windows.Media.Playback.MediaPlayer;

namespace RadioPlayer;

public partial class MainWindow : Window
{
    private const string PlayIcon = "";
    private const string PauseIcon = "";
    private const string VolumeIcon = "";
    private const string MuteIcon = "";

    /// <summary>How long playback may sit in "connecting" before the stream is reopened.</summary>
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly List<Station> _stations;
    private readonly Settings _settings;
    private readonly WinPlayer _player = new();
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(1) };

    private Station? _current;
    private bool _wantPlaying;      // what the listener asked for, regardless of network state
    private bool _retrying;
    private DateTime? _reopenAt;    // when the watchdog should reopen a stream that isn't playing

    public MainWindow()
    {
        InitializeComponent();

        _stations = Station.Load();
        _settings = Settings.Load();
        StationList.ItemsSource = _stations;

        _current = _stations.FirstOrDefault(s => s.Name == _settings.LastStation) ?? _stations.FirstOrDefault();
        Volume.Value = Math.Clamp(_settings.Volume, 0, 100);
        ApplyVolume(); // explicitly: setting the slider to the value it already has raises no event
        RestorePosition();

        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => OnUi(RefreshUi);
        _player.MediaFailed += (_, _) => OnUi(() => ScheduleReopen(RetryDelay, retrying: true));
        _player.MediaEnded += (_, _) => OnUi(() => ScheduleReopen(TimeSpan.Zero, retrying: true));

        // Keyboard media keys / the Windows media flyout. Play always reopens the stream so it
        // starts at the live edge instead of resuming where it was paused.
        var keys = _player.CommandManager;
        keys.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        keys.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        keys.PlayReceived += (_, e) => { e.Handled = true; OnUi(() => PlayLive(_current)); };
        keys.PauseReceived += (_, e) => { e.Handled = true; OnUi(Pause); };
        keys.NextReceived += (_, e) => { e.Handled = true; OnUi(() => PlayLive(Neighbour(+1))); };
        keys.PreviousReceived += (_, e) => { e.Handled = true; OnUi(() => PlayLive(Neighbour(-1))); };

        _watchdog.Tick += (_, _) => CheckStream();
        _watchdog.Start();

        PreviewKeyDown += OnKey;
        PreviewMouseWheel += (_, e) => Volume.Value += e.Delta > 0 ? 5 : -5;
        Loaded += (_, _) => AutoPlayFromCommandLine();
        Closing += (_, _) => Shutdown();

        RefreshUi();
    }

    // ---- playback ---------------------------------------------------------------------------

    /// <summary>Opens the station from scratch, which puts playback at the newest audio available.</summary>
    private void PlayLive(Station? station)
    {
        if (station is null) return;

        _current = station;
        _wantPlaying = true;
        _retrying = false;
        _reopenAt = DateTime.UtcNow + StallLimit;

        var previous = _player.Source as MediaPlaybackItem;

        var item = new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(station.Url)));
        var info = item.GetDisplayProperties();
        info.Type = MediaPlaybackType.Music;
        info.MusicProperties.Title = station.Name;
        info.MusicProperties.Artist = "Live radio";
        item.ApplyDisplayProperties(info);

        _player.Source = item;
        _player.Play();
        previous?.Source.Dispose();

        RefreshUi();
    }

    private void Pause()
    {
        _wantPlaying = false;
        _retrying = false;
        _reopenAt = null;
        _player.Pause();
        RefreshUi();
    }

    private void Toggle()
    {
        if (_wantPlaying) Pause();
        else PlayLive(_current);
    }

    private void ScheduleReopen(TimeSpan delay, bool retrying)
    {
        if (!_wantPlaying) return;
        _retrying = retrying;
        _reopenAt = DateTime.UtcNow + delay;
        RefreshUi();
    }

    /// <summary>Runs every second: reopens the stream if it failed or has been stuck connecting.</summary>
    private void CheckStream()
    {
        if (!_wantPlaying) return;

        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _reopenAt = null;
            return;
        }

        _reopenAt ??= DateTime.UtcNow + StallLimit;
        if (DateTime.UtcNow < _reopenAt) return;

        PlayLive(_current);
        _retrying = true;
        RefreshUi();
    }

    private Station? Neighbour(int step)
    {
        if (_stations.Count == 0) return null;
        int i = _current is null ? 0 : _stations.IndexOf(_current) + step;
        return _stations[(i + _stations.Count) % _stations.Count];
    }

    // ---- UI ---------------------------------------------------------------------------------

    private void RefreshUi()
    {
        bool playing = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        foreach (var s in _stations)
        {
            bool isCurrent = s == _current;
            s.IsSelected = isCurrent;
            s.IsLive = isCurrent && _wantPlaying && playing;
            s.Status = !isCurrent ? ""
                : _wantPlaying ? (playing ? "LIVE" : _retrying ? "RECONNECTING…" : "CONNECTING…")
                : _player.Source is not null ? "PAUSED"
                : "";
        }

        PlayGlyph.Text = _wantPlaying ? PauseIcon : PlayIcon;
        PlayGlyph.Margin = _wantPlaying ? new Thickness(0) : new Thickness(2, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(PlayPause, _wantPlaying ? "Pause" : "Play");
        VolumeGlyph.Text = _player.IsMuted || Volume.Value == 0 ? MuteIcon : VolumeIcon;
        Title = _current is not null && _wantPlaying ? $"{_current.Name} – Radio" : "Radio";
    }

    private void Station_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Station station) return;

        // Clicking the station that is already on pauses it; anything else starts it live.
        if (station == _current && _wantPlaying) Pause();
        else PlayLive(station);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => Toggle();

    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        _player.IsMuted = !_player.IsMuted;
        RefreshUi();
    }

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return; // still loading XAML; the constructor applies the saved volume
        if (e.NewValue > 0) _player.IsMuted = false;
        ApplyVolume();
        RefreshUi();
    }

    private void ApplyVolume()
    {
        // Squared so the slider feels even to the ear rather than bunching all the change at the bottom.
        double level = Volume.Value / 100.0;
        _player.Volume = level * level;
        VolumeText.Text = Math.Round(Volume.Value).ToString();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space: Toggle(); break;
            case Key.M: ToggleMute(); break;
            case Key.Up or Key.Right: Volume.Value += 5; break;
            case Key.Down or Key.Left: Volume.Value -= 5; break;
            case >= Key.D1 and <= Key.D9 when e.Key - Key.D1 < _stations.Count:
                PlayLive(_stations[e.Key - Key.D1]); break;
            case >= Key.NumPad1 and <= Key.NumPad9 when e.Key - Key.NumPad1 < _stations.Count:
                PlayLive(_stations[e.Key - Key.NumPad1]); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnUi(Action action) => Dispatcher.BeginInvoke(action);

    // ---- startup / shutdown -----------------------------------------------------------------

    /// <summary>"RadioPlayer.exe --play 2" or "--play "LRT Opus"" starts a station straight away.</summary>
    private void AutoPlayFromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "--play");
        if (at < 0) return;

        string? which = at + 1 < args.Length ? args[at + 1] : null;
        Station? station =
            int.TryParse(which, out int n) && n >= 1 && n <= _stations.Count ? _stations[n - 1]
            : _stations.FirstOrDefault(s => string.Equals(s.Name, which, StringComparison.OrdinalIgnoreCase))
              ?? _current;
        PlayLive(station);
    }

    private void RestorePosition()
    {
        if (_settings.Left is not double left || _settings.Top is not double top) return;

        bool onScreen = left >= SystemParameters.VirtualScreenLeft
            && top >= SystemParameters.VirtualScreenTop
            && left + Width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top + 100 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (!onScreen) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void Shutdown()
    {
        _watchdog.Stop();

        _settings.Volume = Volume.Value;
        _settings.LastStation = _current?.Name;
        if (WindowState == WindowState.Normal)
        {
            _settings.Left = Left;
            _settings.Top = Top;
        }
        _settings.Save();

        _player.Dispose();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int on = 1; // dark title bar to match the window
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>A station as listed in stations.json, plus the bits of state its row displays.</summary>
public sealed class Station : INotifyPropertyChanged
{
    private bool _isSelected, _isLive;
    private string _status = "";

    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public string Url { get; set; } = "";

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsLive { get => _isLive; set => Set(ref _isLive, value); }
    public string Status { get => _status; set => Set(ref _status, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads stations.json from beside the exe; falls back to the built-in three.</summary>
    public static List<Station> Load()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "stations.json");
            var list = JsonSerializer.Deserialize<List<Station>>(File.ReadAllText(path), Json);
            var usable = list?.Where(s => Uri.TryCreate(s.Url, UriKind.Absolute, out _) && s.Name != "").ToList();
            if (usable is { Count: > 0 }) return usable;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Missing or broken file: use the defaults below.
        }

        return
        [
            new() { Name = "M-1", Note = "m-1.fm", Url = "https://video.m-1.fm/m-1_audio/m-1_audio.stream/playlist.m3u8" },
            new() { Name = "LRT Opus", Note = "lrt.lt", Url = "https://stream-live.lrt.lt/radio_opus/lrt_opus.m3u8" },
            new() { Name = "LRT Radijas", Note = "lrt.lt", Url = "https://stream-live.lrt.lt/radio_radijas/lrt_radijas.m3u8" },
        ];
    }
}

/// <summary>Remembered between runs in %AppData%\RadioPlayer\settings.json.</summary>
public sealed class Settings
{
    public double Volume { get; set; } = 50;
    public string? LastStation { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPlayer", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the volume is not worth an error on exit.
        }
    }
}
