using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace RadioPlayer;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one player at a time: a second launch just brings the first to the front.
        _singleInstance = new Mutex(true, "RadioPlayer.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            BringExistingToFront();
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private static void BringExistingToFront()
    {
        using var me = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(me.ProcessName))
        {
            if (other.Id == me.Id || other.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(other.MainWindowHandle, 9 /* SW_RESTORE */);
            SetForegroundWindow(other.MainWindowHandle);
            break;
        }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
