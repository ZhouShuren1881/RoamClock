namespace RoamClock.WeatherFetch;

/// <summary>A successful weather result. Temperatures are Celsius; wind is km/h.</summary>
public sealed record WeatherSnapshot(
    string City,
    string Location,
    double Latitude,
    double Longitude,
    double Temperature,
    double ApparentTemperature,
    double Humidity,
    double WindSpeed,
    int WeatherCode,
    bool IsDay,
    DateTimeOffset UpdatedAt);

/// <summary>Snapshot is kept only in memory and remains available when refresh fails for the same city.</summary>
public sealed record WeatherFetchState(
    string City,
    WeatherSnapshot? Snapshot = null,
    bool IsRefreshing = false,
    string? LastError = null)
{
    public bool IsStale => Snapshot is not null &&
        (LastError is not null || DateTimeOffset.UtcNow - Snapshot.UpdatedAt >= TimeSpan.FromMinutes(30));
}

public sealed record WeatherFetchOptions
{
    public string City { get; init; } = "Singapore";
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMinutes(2);
}
