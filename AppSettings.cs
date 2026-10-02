using System.IO;
using System.Text.Json;

namespace RoamClock;

public sealed record AppSettings
{
    public string TimeZoneId { get; init; } = "Singapore Standard Time";
    public string City { get; init; } = "Singapore";
    public bool ShowSeconds { get; init; }
    public bool ShowWeather { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public int Width { get; init; } = 154;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoamClock", "settings.json");
    public static AppSettings Load(string? path = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? DefaultPath));
            return settings is not null && !string.IsNullOrWhiteSpace(settings.City) && settings.City.Length <= 120 &&
                !string.IsNullOrWhiteSpace(settings.TimeZoneId) && settings.Width is >= 50 and <= 320
                ? settings : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
