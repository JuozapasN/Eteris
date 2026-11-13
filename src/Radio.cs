using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using WinPlayer = Windows.Media.Playback.MediaPlayer;

namespace RadioPlayer;

/// <summary>
/// The player itself: the station list, what is playing, and the volume. The main window, the mini
/// player and the tray icon are all just views of this one object.
/// </summary>
public sealed class Radio : INotifyPropertyChanged, IDisposable
{
    private const string PlayIcon = "";
    private const string PauseIcon = "";
    private const string VolumeIcon = "";
    private const string MuteIcon = "";

    /// <summary>How long playback may sit in "connecting" before the stream is reopened.</summary>
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BufferGrace = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan NowPlayingInterval = TimeSpan.FromSeconds(30);

    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly WinPlayer _player = new();
    private readonly NowPlayingService _nowPlaying = new();
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _nowPlayingTimer = new() { Interval = NowPlayingInterval };

    private CancellationTokenSource? _nowPlayingRequest;
    private Station? _current;
    private bool _wantPlaying;      // what the listener asked for, regardless of network state
    private bool _retrying;
    private DateTime? _reopenAt;    // when the watchdog should reopen a stream that isn't playing
    private bool _wasPlaying;
    private DateTime? _rebufferingSince; // when a stream that was playing last stopped to buffer
    private double _volume;

    public Radio(IEnumerable<Station> stations, string? lastStation, double volume)
    {
        Stations = new ObservableCollection<Station>(stations);
        _current = Stations.FirstOrDefault(s => s.Name == lastStation) ?? Stations.FirstOrDefault();

        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => OnUi(OnPlaybackStateChanged);
        _player.MediaFailed += (_, _) => OnUi(() => ScheduleReopen(RetryDelay));
        _player.MediaEnded += (_, _) => OnUi(() => ScheduleReopen(TimeSpan.Zero));

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
        _nowPlayingTimer.Tick += (_, _) => UpdateNowPlaying();

        Volume = volume;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---- state the views bind to --------------------------------------------------------------

    public ObservableCollection<Station> Stations { get; }
    public Station? Current => _current;
    public bool WantPlaying => _wantPlaying;
    public bool HasStations => Stations.Count > 0;

    /// <summary>
    /// Playing, or only just stopped to rebuffer: direct streams do that for a moment now and then,
    /// and it shouldn't flash "connecting" each time.
    /// </summary>
    private bool IsLive
    {
        get
        {
            if (!_wantPlaying) return false;
            var state = _player.PlaybackSession.PlaybackState;
            return state == MediaPlaybackState.Playing
                || (state == MediaPlaybackState.Buffering && DateTime.UtcNow - _rebufferingSince < BufferGrace);
        }
    }

    /// <summary>The current station's name, or the app's when there is none.</summary>
    public string Headline => _current?.Name ?? "Eteris";

    /// <summary>One line describing what is happening: the song or programme, or the connection state.</summary>
    public string Detail =>
        _current is null ? "No stations yet"
        : !_wantPlaying ? (_player.Source is null ? _current.Note : "Paused")
        : !IsLive ? (_retrying ? "Reconnecting…" : "Connecting…")
        : _current.Playing.Length > 0 ? _current.Playing
        : "Live";

    public string WindowTitle => _current is not null && _wantPlaying ? $"{_current.Name} – Eteris" : "Eteris";
    public string PlayGlyph => _wantPlaying ? PauseIcon : PlayIcon;
    public string PlayLabel => _wantPlaying ? "Pause" : "Play";
    public string VolumeGlyph => _player.IsMuted || _volume == 0 ? MuteIcon : VolumeIcon;
    public string VolumeText => Math.Round(_volume).ToString();

    /// <summary>0–100, as shown on the slider.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(double.IsNaN(value) ? 50 : value, 0, 100);
            // Squared so the slider feels even to the ear rather than bunching all the change at the bottom.
            double level = _volume / 100.0;
            _player.Volume = level * level;
            if (_volume > 0) _player.IsMuted = false;
            Refresh();
        }
    }

    public bool IsMuted
    {
        get => _player.IsMuted;
        set { _player.IsMuted = value; Refresh(); }
    }

    // ---- playback -----------------------------------------------------------------------------

    /// <summary>Opens the station from scratch, which puts playback at the newest audio available.</summary>
    public void PlayLive(Station? station)
    {
        if (station is null || !Stations.Contains(station)) return;

        if (_current is not null && _current != station) _current.Playing = "";
        _current = station;
        _wantPlaying = true;
        _retrying = false;
        OpenStream(station);
        Refresh();

        _nowPlayingTimer.Stop();
        _nowPlayingTimer.Start();
        UpdateNowPlaying();
    }

    public void Pause()
    {
        _wantPlaying = false;
        _retrying = false;
        _reopenAt = null;
        _player.Pause();

        _nowPlayingTimer.Stop();
        _nowPlayingRequest?.Cancel();
        if (_current is not null) _current.Playing = "";
        Refresh();
    }

    public void Toggle()
    {
        if (_wantPlaying) Pause();
        else PlayLive(_current);
    }

    private void OnPlaybackStateChanged()
    {
        bool playing = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        if (_wasPlaying && !playing) _rebufferingSince = DateTime.UtcNow;
        _wasPlaying = playing;
        Refresh();
    }

    private void OpenStream(Station station)
    {
        _reopenAt = DateTime.UtcNow + StallLimit;
        _wasPlaying = false;
        _rebufferingSince = null;
        var previous = _player.Source as MediaPlaybackItem;

        _player.Source = new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(station.Url)));
        UpdateMediaInfo();
        _player.Play();
        previous?.Source.Dispose();
    }

    private void CloseStream()
    {
        var previous = _player.Source as MediaPlaybackItem;
        _player.Source = null;
        previous?.Source.Dispose();
    }

    private void ScheduleReopen(TimeSpan delay)
    {
        if (!_wantPlaying) return;
        _retrying = true;
        _reopenAt = DateTime.UtcNow + delay;
        Refresh();
    }

    /// <summary>Runs every second: reopens the stream if it failed or has been stuck connecting.</summary>
    private void CheckStream()
    {
        if (!_wantPlaying || _current is null) return;

        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _reopenAt = null;
            return;
        }

        _reopenAt ??= DateTime.UtcNow + StallLimit;
        if (DateTime.UtcNow < _reopenAt)
        {
            Refresh(); // the rebuffering grace period may have just run out
            return;
        }

        _retrying = true;
        OpenStream(_current);
        Refresh();
    }

    private Station? Neighbour(int step)
    {
        if (Stations.Count == 0) return null;
        int i = _current is null ? 0 : Stations.IndexOf(_current) + step;
        return Stations[(i % Stations.Count + Stations.Count) % Stations.Count];
    }

    // ---- now playing --------------------------------------------------------------------------

    private async void UpdateNowPlaying()
    {
        var station = _current;
        if (station is null || !_wantPlaying) return;

        _nowPlayingRequest?.Cancel();
        var request = _nowPlayingRequest = new CancellationTokenSource();

        string? text;
        try
        {
            text = await _nowPlaying.GetAsync(station, request.Token);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer request, or paused
        }

        if (request.IsCancellationRequested || station != _current || !_wantPlaying) return;
        station.Playing = text ?? "";
        UpdateMediaInfo();
        Refresh();
    }

    /// <summary>What Windows shows in its media flyout and on the lock screen.</summary>
    private void UpdateMediaInfo()
    {
        if (_player.Source is not MediaPlaybackItem item || _current is null) return;

        bool known = _current.Playing.Length > 0;
        var info = item.GetDisplayProperties();
        info.Type = MediaPlaybackType.Music;
        info.MusicProperties.Title = known ? _current.Playing : _current.Name;
        info.MusicProperties.Artist = known ? _current.Name : "Live radio";
        item.ApplyDisplayProperties(info);
    }

    // ---- editing the station list -------------------------------------------------------------

    /// <summary>Adds a station; false if one with the same stream address is already there.</summary>
    public bool Add(Station station)
    {
        if (Stations.Any(s => string.Equals(s.Url, station.Url, StringComparison.OrdinalIgnoreCase))) return false;

        Stations.Add(station);
        _current ??= station;
        StationStore.Save(Stations);
        Refresh();
        return true;
    }

    public void Remove(Station station)
    {
        int index = Stations.IndexOf(station);
        if (index < 0) return;

        if (station == _current)
        {
            Pause();
            CloseStream();
            _current = null;
        }

        Stations.RemoveAt(index);
        if (_current is null && Stations.Count > 0) _current = Stations[Math.Min(index, Stations.Count - 1)];
        StationStore.Save(Stations);
        Refresh();
    }

    public void Move(Station station, int step)
    {
        int from = Stations.IndexOf(station);
        int to = from + step;
        if (from < 0 || to < 0 || to >= Stations.Count) return;

        Stations.Move(from, to);
        StationStore.Save(Stations);
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private void Refresh()
    {
        bool live = IsLive;
        foreach (var s in Stations)
        {
            bool isCurrent = s == _current;
            s.IsSelected = isCurrent;
            s.IsLive = isCurrent && live;
            s.Status = !isCurrent ? ""
                : _wantPlaying ? (live ? "LIVE" : _retrying ? "RECONNECTING…" : "CONNECTING…")
                : _player.Source is not null ? "PAUSED"
                : "";
        }

        // An empty name tells bound views that every property may have changed.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void OnUi(Action action) => _ui.BeginInvoke(action);

    public void Dispose()
    {
        _watchdog.Stop();
        _nowPlayingTimer.Stop();
        _nowPlayingRequest?.Cancel();
        _player.Dispose();
    }
}

/// <summary>Keyboard shortcuts shared by the main window and the mini player.</summary>
internal static class Shortcuts
{
    /// <returns>True if the key was one of ours.</returns>
    public static bool Handle(Radio radio, Key key)
    {
        switch (key)
        {
            case Key.Space: radio.Toggle(); return true;
            case Key.M: radio.IsMuted = !radio.IsMuted; return true;
            case Key.Up or Key.Right: radio.Volume += 5; return true;
            case Key.Down or Key.Left: radio.Volume -= 5; return true;
            case >= Key.D1 and <= Key.D9 when key - Key.D1 < radio.Stations.Count:
                radio.PlayLive(radio.Stations[key - Key.D1]); return true;
            case >= Key.NumPad1 and <= Key.NumPad9 when key - Key.NumPad1 < radio.Stations.Count:
                radio.PlayLive(radio.Stations[key - Key.NumPad1]); return true;
            default: return false;
        }
    }
}
