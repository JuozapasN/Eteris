using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioPlayer;

/// <summary>A search hit from the station directory, ready to show in a list.</summary>
public sealed record DirectoryStation(string Name, string Url, string Details, string Note);

/// <summary>Searches radio-browser.info, a free community-run directory of internet radio stations.</summary>
public static class StationDirectory
{
    // The directory is a handful of mirrors; any one of them can answer.
    private static readonly string[] Hosts =
    [
        "de1.api.radio-browser.info",
        "fi1.api.radio-browser.info",
        "de2.api.radio-browser.info",
        "all.api.radio-browser.info",
    ];

    /// <summary>Formats the Windows media engine plays out of the box.</summary>
    private static readonly HashSet<string> PlayableCodecs = new(StringComparer.OrdinalIgnoreCase) { "MP3", "AAC", "AAC+" };

    public static async Task<List<DirectoryStation>> SearchAsync(string query, CancellationToken ct)
    {
        string path = "/json/stations/search?limit=80&hidebroken=true&order=clickcount&reverse=true&name="
                      + Uri.EscapeDataString(query);

        Exception? lastError = null;
        foreach (string host in Hosts)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var rows = await Http.Client.GetFromJsonAsync<List<Row>>($"https://{host}{path}", timeout.Token);
                return Convert(rows ?? []);
            }
            catch (Exception e) when ((e is HttpRequestException or JsonException or OperationCanceledException)
                                      && !ct.IsCancellationRequested)
            {
                lastError = e; // try the next mirror
            }
        }

        throw new HttpRequestException("No station directory server could be reached.", lastError);
    }

    private static List<DirectoryStation> Convert(List<Row> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DirectoryStation>();

        foreach (var row in rows)
        {
            string name = (row.Name ?? "").Trim();
            string url = string.IsNullOrWhiteSpace(row.UrlResolved) ? (row.Url ?? "") : row.UrlResolved;
            bool playable = row.Hls == 1 || PlayableCodecs.Contains(row.Codec ?? "");
            if (name == "" || !playable || !StationStore.IsStreamUrl(url) || !seen.Add(url)) continue;

            string format = row.Hls == 1 ? "HLS" : row.Codec!.ToUpperInvariant();
            if (row.Bitrate > 0) format += $" {row.Bitrate} kbps";
            string details = string.Join(" · ", new[] { row.Country, format }.Where(s => !string.IsNullOrWhiteSpace(s)));

            string note = Uri.TryCreate(row.Homepage, UriKind.Absolute, out var home)
                ? (home.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? home.Host[4..] : home.Host)
                : row.Country ?? "";

            result.Add(new DirectoryStation(name, url, details, note));
            if (result.Count == 40) break;
        }

        return result;
    }

    private sealed class Row
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("url_resolved")] public string? UrlResolved { get; set; }
        [JsonPropertyName("homepage")] public string? Homepage { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("codec")] public string? Codec { get; set; }
        [JsonPropertyName("bitrate")] public int Bitrate { get; set; }
        [JsonPropertyName("hls")] public int Hls { get; set; }
    }
}
