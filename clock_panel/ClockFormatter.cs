using System.Globalization;
using RoamClock.Adpater;
using RoamClock.WeatherFetch;

namespace RoamClock.Components;

/// <summary>The WPF preview and native taskbar consume the same formatted snapshot.</summary>
public static class ClockFormatter
{
    public static ClockContent Format(DateTimeOffset utcNow, string timeZoneId, bool showSeconds,
        bool showWeather, WeatherFetchState? state, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        TimeZoneInfo timeZone;
        string? zoneError = null;
        try { timeZone = string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Local;
            zoneError = $"无效时区 {timeZoneId}；正在显示本地时间。";
        }
        var now = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var offset = now.Offset;
        var zone = $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
        var weather = state?.Snapshot;
        var temperature = weather is null ? "--" : $"{weather.Temperature.ToString("0", culture)}°C";
        var status = state?.IsRefreshing == true ? "正在更新天气…" : state?.LastError;
        var tooltip = $"{now.ToString("F", culture)}\n{timeZone.DisplayName}\n{zone}";
        if (zoneError is not null) tooltip += $"\n{zoneError}";
        if (showWeather)
        {
            tooltip += weather is null ? $"\n{state?.City ?? "天气"} · {status ?? "等待天气数据"}"
                : $"\n{weather.Location} · {WeatherConditions.Describe(weather.WeatherCode)} {temperature}" +
                  $"\n体感 {weather.ApparentTemperature:0}°C · 湿度 {weather.Humidity:0}% · 风速 {weather.WindSpeed:0} km/h" +
                  $"\n更新于 {weather.UpdatedAt.ToLocalTime():g}" + (state!.IsStale ? " · 天气已过期" : "") +
                  (status is null ? "" : $"\n{status}") + "\n天气来源：Open-Meteo";
        }
        if (tooltip.Length > 1023) tooltip = tooltip[..1022] + "…";
        return new(now.ToString(showSeconds ? "HH:mm:ss" : "HH:mm", culture), now.ToString("MM/dd ddd", culture),
            zone, temperature, WeatherConditions.IconPath(weather?.WeatherCode ?? -1, weather?.IsDay ?? true),
            tooltip, showWeather, state?.IsStale == true);
    }
}
