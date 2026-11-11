using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RadioPlayer;

internal static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RadioPlayer/1.1");
        return client;
    }
}

/// <summary>Finds out what a station is playing right now. Returns null when it can't tell.</summary>
internal sealed partial class NowPlayingService
{
    private const string LrtProgrammeUrl = "https://www.lrt.lt/static/tvprog/tvprog.json";
    private static readonly TimeSpan LrtCacheTime = TimeSpan.FromSeconds(60);

    private Dictionary<string, string>? _lrtProgrammes;
    private DateTime _lrtFetchedAt;

    public async Task<string?> GetAsync(Station station, CancellationToken ct)
    {
        try
        {
            (string kind, string value) = Source(station);
            return kind switch
            {
                "icy" => Clean(await ReadIcyTitleAsync(value, ct), station.Name),
                "lrt" => Clean(await ReadLrtProgrammeAsync(value, ct), station.Name),
                _ => null,
            };
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException
                                      or InvalidOperationException or KeyNotFoundException
                                  || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null; // unreachable, timed out or not in the expected shape: simply show nothing
        }
    }

    /// <summary>Splits "icy:https://…" / "lrt:Opus"; with nothing configured, direct streams are asked themselves.</summary>
    private static (string Kind, string Value) Source(Station station)
    {
        if (!string.IsNullOrWhiteSpace(station.NowPlaying))
        {
            int colon = station.NowPlaying.IndexOf(':');
            return colon > 0
                ? (station.NowPlaying[..colon].Trim().ToLowerInvariant(), station.NowPlaying[(colon + 1)..].Trim())
                : ("", "");
        }

        bool isHls = Uri.TryCreate(station.Url, UriKind.Absolute, out var uri)
            && uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
        return isHls ? ("", "") : ("icy", station.Url);
    }

    // ---- ICY (Icecast / Shoutcast "StreamTitle" metadata embedded in the audio stream) ----------

    private static async Task<string?> ReadIcyTitleAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        using var response = await Http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) return null;

        // The server sends this many bytes of audio between metadata blocks.
        if (!response.Headers.TryGetValues("icy-metaint", out var values)
            || !int.TryParse(values.FirstOrDefault(), out int interval)
            || interval is <= 0 or > 1 << 20)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        byte[] buffer = new byte[Math.Max(interval, 4080)];
        await stream.ReadExactlyAsync(buffer.AsMemory(0, interval), timeout.Token);   // skip the audio
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 1), timeout.Token);          // length byte, in 16-byte units
        int length = buffer[0] * 16;
        if (length == 0) return null;
        await stream.ReadExactlyAsync(buffer.AsMemory(0, length), timeout.Token);

        var match = StreamTitle().Match(Decode(buffer.AsSpan(0, length)));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        bytes = bytes.TrimEnd((byte)0);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes); // older servers
        }
    }

    [GeneratedRegex("StreamTitle='(.*?)';", RegexOptions.Singleline)]
    private static partial Regex StreamTitle();

    // ---- LRT (the broadcaster's programme guide: gives the show that is on, not individual songs) ----

    private async Task<string?> ReadLrtProgrammeAsync(string channel, CancellationToken ct)
    {
        if (_lrtProgrammes is null || DateTime.UtcNow - _lrtFetchedAt > LrtCacheTime)
        {
            await using var stream = await Http.Client.GetStreamAsync(LrtProgrammeUrl, ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var programmes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.GetProperty("tvprog").GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("channel", out var name) && name.ValueKind == JsonValueKind.String
                    && item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                {
                    programmes[name.GetString()!] = title.GetString()!;
                }
            }

            _lrtProgrammes = programmes;
            _lrtFetchedAt = DateTime.UtcNow;
        }

        return _lrtProgrammes.GetValueOrDefault(channel);
    }

    // ---- shared ---------------------------------------------------------------------------------

    /// <summary>Drops empty titles and ones that only repeat the station's name ("M1" on M-1).</summary>
    private static string? Clean(string? title, string stationName)
    {
        title = title?.Trim().Trim('-').Trim();
        if (string.IsNullOrEmpty(title)) return null;
        string letters = Letters(title);
        return letters.Length == 0 || letters == Letters(stationName) ? null : title;
    }

    private static string Letters(string text) =>
        new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
