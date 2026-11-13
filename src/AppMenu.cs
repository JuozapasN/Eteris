using System.Windows.Controls;

namespace RadioPlayer;

/// <summary>The right-click menu shared by the tray icon and the mini player.</summary>
internal static class AppMenu
{
    public static ContextMenu Build(App app)
    {
        var radio = app.Radio;
        var menu = new ContextMenu();

        foreach (var station in radio.Stations)
        {
            menu.Items.Add(Item(station.Name, () => radio.PlayLive(station), isChecked: station == radio.Current));
        }
        if (radio.HasStations)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(radio.PlayLabel, radio.Toggle));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Item("Open Eteris", app.ShowMain));
        menu.Items.Add(Item("Mini player", app.ShowMini));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", app.Quit));
        return menu;
    }

    private static MenuItem Item(string text, Action action, bool isChecked = false)
    {
        var item = new MenuItem { Header = text, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }
}
