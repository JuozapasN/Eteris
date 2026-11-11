using System.IO;
using System.Text.Json;

namespace RadioPlayer;

/// <summary>Remembered between runs in %AppData%\RadioPlayer\settings.json.</summary>
public sealed class Settings
{
    public double Volume { get; set; } = 50;
    public string? LastStation { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>True when the app was last used as the small always-on-top strip.</summary>
    public bool MiniMode { get; set; }
    public double? MiniLeft { get; set; }
    public double? MiniTop { get; set; }

    /// <summary>The "still running in the tray" notification is only shown once.</summary>
    public bool TrayHintShown { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPlayer", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the volume is not worth an error.
        }
    }
}
