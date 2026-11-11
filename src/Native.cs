using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RadioPlayer;

/// <summary>The few Win32 calls WPF has no equivalent for.</summary>
internal static class Native
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    public const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dark title bar to match the window. Call once the window has a handle.</summary>
    public static void UseDarkTitleBar(Window window) => SetAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);

    /// <summary>Windows 11 rounded corners for a window without a title bar. No effect on Windows 10.</summary>
    public static void RoundCorners(Window window) => SetAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    private static void SetAttribute(Window window, int attribute, int value) =>
        DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, attribute, ref value, sizeof(int));

    /// <summary>True if a window of this size at this position would be fully on one of the screens.</summary>
    public static bool IsOnScreen(double left, double top, double width, double height) =>
        left >= SystemParameters.VirtualScreenLeft
        && top >= SystemParameters.VirtualScreenTop
        && left + width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
        && top + height <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
}
