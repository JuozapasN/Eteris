using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace RadioPlayer;

/// <summary>
/// The icon in the notification area. Left-click shows or hides the player, right-click opens the
/// menu, and the tooltip says what is playing.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly Forms.NotifyIcon _icon;
    private ContextMenu? _menu;
    private Window? _menuHost;

    public TrayIcon(App app)
    {
        _app = app;
        _icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "Eteris", Visible = true };
        _icon.MouseUp += OnMouseUp;
        app.Radio.PropertyChanged += OnRadioChanged;
        UpdateTooltip();
    }

    /// <summary>Tells the listener, once, that closing the window did not stop the radio.</summary>
    public void ShowHint() => _icon.ShowBalloonTip(
        5000,
        "Eteris is still running",
        "It keeps playing from the notification area. Click its icon to open it, or right-click it to exit.",
        Forms.ToolTipIcon.None);

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) _app.ToggleVisibility();
        else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
    }

    private void ShowMenu()
    {
        if (_menu is not null) _menu.IsOpen = false;

        // A WPF menu only closes on Escape or a click elsewhere while one of the app's windows is
        // active, and from the tray there may be none on screen. So an invisible window is made
        // active for as long as the menu is open.
        _menuHost ??= new Window
        {
            Width = 0, Height = 0, Left = -32000, Top = -32000,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Opacity = 0, ShowInTaskbar = false,
        };
        _menuHost.Show();
        _menuHost.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(_menuHost).Handle);

        var menu = _menu = AppMenu.Build(_app);
        menu.PlacementTarget = _menuHost;
        menu.Placement = PlacementMode.MousePoint;
        menu.Closed += (_, _) =>
        {
            if (_menu != menu) return;
            _menu = null;
            _menuHost.Hide();
        };
        menu.IsOpen = true;
    }

    private void OnRadioChanged(object? sender, PropertyChangedEventArgs e) => UpdateTooltip();

    private void UpdateTooltip()
    {
        var radio = _app.Radio;
        string text = radio.Current is null ? "Eteris" : $"{radio.Headline} – {radio.Detail}";
        if (text.Length > 100) text = text[..99] + "…"; // NotifyIcon rejects long tooltips
        if (_icon.Text != text) _icon.Text = text;
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or Win32Exception)
        {
            // Fall through to the exe's own icon.
        }

        return Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _app.Radio.PropertyChanged -= OnRadioChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
