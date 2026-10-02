using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RoamClock.WeatherFetch;

/// <summary>
/// Background-only provider. Consumers read State and subscribe to StateChanged;
/// events may run on the worker thread, so UI hosts must dispatch to their UI thread.
/// </summary>
public sealed class WeatherFetchService : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly WeatherFetchOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private WeatherFetchState _state;
    private Task? _worker;
    private long _revision;
    private bool _disposed;
    private bool _refreshRequested;
    private bool _enabled = true;
    private CancellationTokenSource? _requestCancellation;

    public WeatherFetchService(WeatherFetchOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new();
        if (_options.RefreshInterval <= TimeSpan.Zero || _options.RetryInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Refresh and retry intervals must be positive.");
        _state = new(NormalizeCity(_options.City));
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
    }

    public WeatherFetchState State { get { lock (_sync) return _state; } }
    public bool IsEnabled { get { lock (_sync) return _enabled; } }
    public event EventHandler? StateChanged;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _worker ??= Task.Run(() => RunAsync(_lifetime.Token));
        }
    }

    public void SetCity(string city)
    {
        city = NormalizeCity(city);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.Equals(city, _state.City, StringComparison.OrdinalIgnoreCase)) return;
            _revision++;
            // Never show the previous city's weather beside the newly selected city.
            _state = new(city);
            Wake();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enabled == enabled) return;
            _enabled = enabled;
            _revision++;
            _requestCancellation?.Cancel();
            _state = _state with { IsRefreshing = false };
            _refreshRequested = enabled;
            Wake();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRefresh()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_enabled) return;
            _refreshRequested = true;
            Wake();
        }
    }

    private void Wake()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    private async Task RunAsync(CancellationToken token)
    {
        long activeRevision = -1;
        var nextRefresh = DateTimeOffset.MinValue;
        try
        {
            while (!token.IsCancellationRequested)
            {
                string city;
                long revision;
                bool forceRefresh;
                bool enabled;
                lock (_sync)
                {
                    enabled = _enabled;
                    city = _state.City;
                    revision = _revision;
                    forceRefresh = _refreshRequested;
                    _refreshRequested = false;
                    // Drain under the same lock as RequestRefresh to avoid losing its signal.
                    _wake.Wait(0);
                }
                if (!enabled)
                {
                    await _wake.WaitAsync(token).ConfigureAwait(false);
                    continue;
                }
                if (activeRevision != revision)
                {
                    activeRevision = revision;
                    nextRefresh = DateTimeOffset.MinValue;
                }
                if (forceRefresh) nextRefresh = DateTimeOffset.MinValue;

                if (DateTimeOffset.UtcNow >= nextRefresh)
                {
                    CancellationTokenSource requestCancellation;
                    lock (_sync)
                    {
                        if (!_enabled || revision != _revision) continue;
                        requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                        _requestCancellation = requestCancellation;
                    }
                    var previous = State;
                    Publish(revision, previous with { IsRefreshing = true });
                    try
                    {
                        var snapshot = await FetchAsync(city, requestCancellation.Token).ConfigureAwait(false);
                        Publish(revision, new(city, snapshot));
                        nextRefresh = DateTimeOffset.UtcNow + _options.RefreshInterval;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested) { continue; }
                    catch (Exception ex)
                    {
                        // HTTP timeouts are retryable; only application cancellation stops the worker.
                        Publish(revision, previous with { IsRefreshing = false, LastError = ex.Message });
                        nextRefresh = DateTimeOffset.UtcNow + _options.RetryInterval;
                    }
                    finally
                    {
                        lock (_sync)
                        {
                            _requestCancellation = null;
                            requestCancellation.Dispose();
                        }
                    }
                }

                lock (_sync) { if (revision != _revision) continue; }
                var delay = nextRefresh - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero && await _wake.WaitAsync(delay, token).ConfigureAwait(false))
                    nextRefresh = DateTimeOffset.MinValue;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (_ownsHttp) _http.Dispose();
            _wake.Dispose();
            _lifetime.Dispose();
        }
    }

    private void Publish(long revision, WeatherFetchState state)
    {
        lock (_sync)
        {
            if (_disposed || !_enabled || revision != _revision) return;
            _state = state;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<WeatherSnapshot> FetchAsync(string city, CancellationToken token)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var geoUrl = "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={Uri.EscapeDataString(city)}&count=1&language={Uri.EscapeDataString(language)}&format=json";
        var geo = await _http.GetFromJsonAsync<GeoResponse>(geoUrl, token).ConfigureAwait(false);
        var location = geo?.Results?.FirstOrDefault() ?? throw new InvalidOperationException($"找不到城市：{city}");
        if (!double.IsFinite(location.Latitude) || !double.IsFinite(location.Longitude) ||
            Math.Abs(location.Latitude) > 90 || Math.Abs(location.Longitude) > 180)
            throw new InvalidDataException("天气服务返回了无效坐标。");
        var url = "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={location.Latitude.ToString(CultureInfo.InvariantCulture)}" +
            $"&longitude={location.Longitude.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
            "&temperature_unit=celsius&wind_speed_unit=kmh&timezone=auto";
        var weather = await _http.GetFromJsonAsync<ForecastResponse>(url, token).ConfigureAwait(false);
        var current = weather?.Current ?? throw new InvalidDataException("天气服务未返回当前天气。");
        if (current.Temperature is not double temperature || !double.IsFinite(temperature) ||
            current.ApparentTemperature is not double apparent || !double.IsFinite(apparent) ||
            current.Humidity is not double humidity || !double.IsFinite(humidity) ||
            current.WindSpeed is not double wind || !double.IsFinite(wind) ||
            current.WeatherCode is not int code || current.IsDay is not int isDay)
            throw new InvalidDataException("天气服务返回的数据不完整。");
        var region = location.Admin1 ?? location.Country;
        var label = string.IsNullOrWhiteSpace(region) ? location.Name : $"{location.Name}, {region}";
        return new(city, label, location.Latitude, location.Longitude, temperature, apparent,
            humidity, wind, code, isDay == 1, DateTimeOffset.UtcNow);
    }

    private static string NormalizeCity(string city) => !string.IsNullOrWhiteSpace(city)
        ? city.Trim() : throw new ArgumentException("请输入城市名。", nameof(city));

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            if (_worker is null)
            {
                if (_ownsHttp) _http.Dispose();
                _wake.Dispose();
                _lifetime.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_worker is not null) await _worker.ConfigureAwait(false);
    }

    private sealed class GeoResponse
    {
        [JsonPropertyName("results")] public GeoLocation[]? Results { get; set; }
    }
    private sealed class GeoLocation
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("admin1")] public string? Admin1 { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("latitude")] public double Latitude { get; set; } = double.NaN;
        [JsonPropertyName("longitude")] public double Longitude { get; set; } = double.NaN;
    }
    private sealed class ForecastResponse
    {
        [JsonPropertyName("current")] public CurrentWeather? Current { get; set; }
    }
    private sealed class CurrentWeather
    {
        [JsonPropertyName("temperature_2m")] public double? Temperature { get; set; }
        [JsonPropertyName("apparent_temperature")] public double? ApparentTemperature { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public double? Humidity { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double? WindSpeed { get; set; }
        [JsonPropertyName("weather_code")] public int? WeatherCode { get; set; }
        [JsonPropertyName("is_day")] public int? IsDay { get; set; }
    }
}
