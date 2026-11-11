using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RadioPlayer;

/// <summary>Adds a station, either from a directory search or from a stream address typed in.</summary>
public partial class AddStationWindow : Window
{
    private readonly Radio _radio;
    private CancellationTokenSource? _search;

    public AddStationWindow(Radio radio)
    {
        _radio = radio;
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
        Closed += (_, _) => _search?.Cancel();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.UseDarkTitleBar(this);
    }

    // ---- search -------------------------------------------------------------------------------

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Search();
    }

    private void Search_Click(object sender, RoutedEventArgs e) => Search();

    private async void Search()
    {
        string query = SearchBox.Text.Trim();
        if (query.Length < 2)
        {
            SearchStatus.Text = "Type at least two letters.";
            return;
        }

        _search?.Cancel();
        var search = _search = new CancellationTokenSource();
        Results.ItemsSource = null;
        SearchStatus.Text = "Searching…";

        try
        {
            var found = await StationDirectory.SearchAsync(query, search.Token);
            if (search.IsCancellationRequested) return;

            Results.ItemsSource = found;
            SearchStatus.Text = found.Count == 0
                ? "No stations found."
                : $"{found.Count} found. Double-click one to add it.";
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one, or the window closed.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            SearchStatus.Text = "Couldn't reach the station directory. Check your connection and try again.";
        }
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AddSelected.IsEnabled = Results.SelectedItem is DirectoryStation;

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddFromResults();

    private void AddSelected_Click(object sender, RoutedEventArgs e) => AddFromResults();

    private void AddFromResults()
    {
        if (Results.SelectedItem is not DirectoryStation hit) return;
        if (!Add(new Station { Name = hit.Name, Note = hit.Note, Url = hit.Url }))
        {
            SearchStatus.Text = "That station is already in your list.";
        }
    }

    // ---- manual entry -------------------------------------------------------------------------

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddManual();
    }

    private void AddManual_Click(object sender, RoutedEventArgs e) => AddManual();

    private void AddManual()
    {
        string url = UrlBox.Text.Trim();
        if (!StationStore.IsStreamUrl(url))
        {
            ManualStatus.Text = "Enter a full address starting with http:// or https://.";
            return;
        }

        string host = new Uri(url).Host;
        string name = NameBox.Text.Trim();
        if (!Add(new Station { Name = name.Length > 0 ? name : host, Note = host, Url = url }))
        {
            ManualStatus.Text = "That address is already in your list.";
        }
    }

    /// <summary>Adds the station and closes the dialog; false if it was already in the list.</summary>
    private bool Add(Station station)
    {
        if (!_radio.Add(station)) return false;
        DialogResult = true;
        return true;
    }
}
