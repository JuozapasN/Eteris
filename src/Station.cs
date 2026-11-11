using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioPlayer;

/// <summary>A station as stored in stations.json, plus the bits of live state its row displays.</summary>
public sealed class Station : INotifyPropertyChanged
{
    private bool _isSelected, _isLive;
    private string _status = "", _playing = "";

    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>
    /// Where to get the current song/programme: "icy:&lt;stream url&gt;" or "lrt:&lt;channel&gt;".
    /// When absent, direct (non-HLS) streams are asked for their own ICY metadata.
    /// </summary>
    public string? NowPlaying { get; set; }

    [JsonIgnore] public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    [JsonIgnore] public bool IsLive { get => _isLive; set => Set(ref _isLive, value); }
    [JsonIgnore] public string Status { get => _status; set => Set(ref _status, value); }

    /// <summary>What is on air right now; empty when unknown or not playing.</summary>
    [JsonIgnore]
    public string Playing
    {
        get => _playing;
        set { if (Set(ref _playing, value)) Raise(nameof(Subtitle)); }
    }

    /// <summary>Second line of the station row: what's on air if known, otherwise the note.</summary>
    [JsonIgnore] public string Subtitle => _playing.Length > 0 ? _playing : Note;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Loads and saves the station list. The user's own list lives in %AppData%\RadioPlayer\stations.json
/// and is created the first time they add, remove or reorder a station; until then the stations.json
/// shipped beside the exe is used.
/// </summary>
public static class StationStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep ž, š, ė… readable in the file
    };

    public static string UserFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPlayer", "stations.json");

    private static string BundledFile => Path.Combine(AppContext.BaseDirectory, "stations.json");

    public static List<Station> Load() =>
        Read(UserFile, allowEmpty: true) ?? Read(BundledFile, allowEmpty: false) ?? Defaults();

    public static void Save(IEnumerable<Station> stations)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
            File.WriteAllText(UserFile, JsonSerializer.Serialize(stations, WriteOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The list still works for this run; it just won't be remembered.
        }
    }

    private static List<Station>? Read(string path, bool allowEmpty)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<Station>>(File.ReadAllText(path), ReadOptions);
            var usable = list?.Where(s => s.Name != "" && IsStreamUrl(s.Url)).ToList();
            return usable is null || (usable.Count == 0 && !allowEmpty) ? null : usable;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // missing or broken file: fall through to the next source
        }
    }

    public static bool IsStreamUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static List<Station> Defaults() =>
    [
        new() { Name = "M-1", Note = "m-1.fm", Url = "https://video.m-1.fm/m-1_audio/m-1_audio.stream/playlist.m3u8", NowPlaying = "icy:https://stream.m-1.fm/m1/aacp64" },
        new() { Name = "LRT Opus", Note = "lrt.lt", Url = "https://stream-live.lrt.lt/radio_opus/lrt_opus.m3u8", NowPlaying = "lrt:Opus" },
        new() { Name = "LRT Radijas", Note = "lrt.lt", Url = "https://stream-live.lrt.lt/radio_radijas/lrt_radijas.m3u8", NowPlaying = "lrt:LR" },
    ];
}
