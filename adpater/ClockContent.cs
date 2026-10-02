namespace RoamClock.Adpater;

/// <summary>Already formatted display data. Explorer never fetches weather or computes time zones.</summary>
public sealed record ClockContent(string Time, string Date, string Zone, string Temperature,
    string WeatherPath, string Tooltip, bool ShowWeather = true, bool IsStale = false)
{
    internal void Validate()
    {
        ValidateText(Time, 32, nameof(Time));
        ValidateText(Date, 64, nameof(Date));
        ValidateText(Zone, 32, nameof(Zone));
        ValidateText(Temperature, 32, nameof(Temperature));
        ValidateText(WeatherPath, 1024, nameof(WeatherPath));
        ValidateText(Tooltip, 1024, nameof(Tooltip));
    }

    private static void ValidateText(string value, int capacity, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length >= capacity || value.Contains('\0'))
            throw new ArgumentException($"{name} 必须少于 {capacity} 个 UTF-16 字符且不能包含 NUL。", name);
    }
}
