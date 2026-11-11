using System.Windows;

namespace RadioPlayer;

/// <summary>
/// Owns the player and decides which of its views is on screen: the main window, the mini player,
/// or only the tray icon. Closing a window hides it; the app ends through Exit in the tray menu.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = "RadioPlayer.SingleInstance";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal, _exitSignal;
    private Settings _settings = new();
    private MainWindow? _main;
    private MiniWindow? _mini;
    private TrayIcon? _tray;

    public Radio Radio { get; private set; } = null!;

    /// <summary>True once the app is really shutting down, so windows stop turning "close" into "hide".</summary>
    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Only one player at a time. A second launch tells the first to show itself (or, with
        // --exit, to quit) and then ends.
        bool wantsExit = e.Args.Contains("--exit");
        _singleInstance = new Mutex(true, InstanceName, out bool isFirst);
        if (!isFirst)
        {
            SignalRunningInstance(wantsExit ? ".Exit" : ".Show");
            Shutdown();
            return;
        }
        if (wantsExit)
        {
            Shutdown(); // nothing was running
            return;
        }

        _showSignal = Listen(".Show", ShowCurrent);
        _exitSignal = Listen(".Exit", Quit);

        _settings = Settings.Load();
        Radio = new Radio(StationStore.Load(), _settings.LastStation, _settings.Volume);
        _main = new MainWindow(this, _settings);
        _tray = new TrayIcon(this);
        SessionEnding += (_, _) => { IsExiting = true; SaveSettings(); };

        ShowCurrent();
        AutoPlay(e.Args);
    }

    // ---- which view is showing ----------------------------------------------------------------

    public void ShowMain()
    {
        if (_main is null) return;
        _settings.MiniMode = false;
        _mini?.Hide();

        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ShowMini()
    {
        _settings.MiniMode = true;
        _main?.Hide();

        _mini ??= new MiniWindow(this, _settings);
        _mini.Show();
        _mini.Activate();
    }

    private void ShowCurrent()
    {
        if (_settings.MiniMode) ShowMini();
        else ShowMain();
    }

    public void HideToTray()
    {
        SaveSettings();
        _main?.Hide();
        _mini?.Hide();

        if (!_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            _settings.Save();
            _tray?.ShowHint();
        }
    }

    /// <summary>Left-click on the tray icon.</summary>
    public void ToggleVisibility()
    {
        if (_main?.IsVisible == true || _mini?.IsVisible == true) HideToTray();
        else ShowCurrent();
    }

    public void Quit()
    {
        if (IsExiting) return;
        IsExiting = true;

        SaveSettings();
        _tray?.Dispose();
        Radio.Dispose();
        Shutdown();
    }

    // ---- startup helpers ----------------------------------------------------------------------

    /// <summary>"RadioPlayer.exe --play 2" or "--play "LRT Opus"" starts a station straight away.</summary>
    private void AutoPlay(string[] args)
    {
        int at = Array.IndexOf(args, "--play");
        if (at < 0) return;

        string? which = at + 1 < args.Length ? args[at + 1] : null;
        Station? station =
            int.TryParse(which, out int n) && n >= 1 && n <= Radio.Stations.Count ? Radio.Stations[n - 1]
            : Radio.Stations.FirstOrDefault(s => string.Equals(s.Name, which, StringComparison.OrdinalIgnoreCase))
              ?? Radio.Current;
        Radio.PlayLive(station);
    }

    private EventWaitHandle Listen(string suffix, Action action)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + suffix);
        ThreadPool.RegisterWaitForSingleObject(
            signal, (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, executeOnlyOnce: false);
        return signal;
    }

    private static void SignalRunningInstance(string suffix)
    {
        Native.AllowSetForegroundWindow(Native.ASFW_ANY); // let the running copy come to the front
        if (EventWaitHandle.TryOpenExisting(InstanceName + suffix, out var signal))
        {
            using (signal) signal.Set();
        }
    }

    private void SaveSettings()
    {
        if (_main is null) return; // never got as far as starting up

        _settings.Volume = Radio.Volume;
        _settings.LastStation = Radio.Current?.Name;
        if (_main.WindowState == WindowState.Normal && !double.IsNaN(_main.Left) && !double.IsNaN(_main.Top))
        {
            _settings.Left = _main.Left;
            _settings.Top = _main.Top;
        }
        if (_mini is not null)
        {
            _settings.MiniLeft = _mini.Left;
            _settings.MiniTop = _mini.Top;
        }
        _settings.Save();
    }
}
