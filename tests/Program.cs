using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RoamClock;
using RoamClock.Components;
using RoamClock.WeatherFetch;

internal static class Program
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "RoamClock.Tests", Guid.NewGuid().ToString("N"));
    private static int _passed;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Root);
            WeatherTests().GetAwaiter().GetResult();
            IntegrationTests();
            PanelTests(args.FirstOrDefault());
            Console.WriteLine($"PASS: {_passed} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // Only remove this run's generated test directory, beneath our known temp root.
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static async Task WeatherTests()
    {
        var options = new WeatherFetchOptions { City = "San Francisco" };
        var fail = false;
        var calls = 0;
        using var http = new HttpClient(new FakeHandler((request, token) =>
        {
            Interlocked.Increment(ref calls);
            if (fail) throw new HttpRequestException("offline");
            return Task.FromResult(Response(request));
        }));
        WeatherSnapshot snapshot;
        await using (var service = new WeatherFetchService(options, http))
        {
            service.Start();
            await Until(() => service.State.Snapshot is not null);
            snapshot = service.State.Snapshot!;
            Check(snapshot.Temperature == 22.4 && snapshot.WeatherCode == 2, "Parse current weather");
            Check(calls == 2, "Startup fetches fresh weather from HTTP");
            fail = true;
            service.RequestRefresh();
            await Until(() => service.State.LastError is not null);
            Check(service.State.Snapshot == snapshot && service.State.IsStale, "Offline refresh retains last successful weather");
            Check(!service.State.IsRefreshing, "Failed request clears loading state");
        }
        var before = calls;
        await using (var service = new WeatherFetchService(options, http))
        {
            service.Start();
            await Until(() => service.State.LastError is not null);
            Check(service.State.Snapshot is null && calls == before + 1,
                "Offline restart does not restore weather from a previous service");
        }
        fail = false;
        before = calls;
        await using (var service = new WeatherFetchService(options, http))
        {
            service.RequestRefresh();
            service.Start();
            await Until(() => service.State.Snapshot is not null);
            Check(calls == before + 2 && service.State.LastError is null,
                "Refresh requested before Start fetches fresh weather");
            before = calls;
            var previous = service.State.Snapshot;
            service.RequestRefresh();
            await Until(() => service.State.Snapshot != previous);
            Check(calls == before + 2, "Manual refresh fetches new weather despite an in-memory snapshot");
        }
        before = calls;
        await using (var service = new WeatherFetchService(options, http))
        {
            service.Start();
            await Until(() => service.State.Snapshot is not null);
            Check(calls == before + 2 && service.State.LastError is null,
                "Each new service fetches weather again");
        }

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstForecast = true;
        using var slowHttp = new HttpClient(new FakeHandler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "api.open-meteo.com" && firstForecast)
            {
                firstForecast = false;
                entered.SetResult();
                await release.Task.WaitAsync(token);
            }
            return Response(request);
        }));
        await using (var service = new WeatherFetchService(options, slowHttp))
        {
            var wrongCityShown = false;
            service.StateChanged += (_, _) =>
            {
                var state = service.State;
                if (state.City == "Tokyo" && state.Snapshot?.City == "San Francisco") wrongCityShown = true;
            };
            service.Start();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            service.SetCity("Tokyo");
            Check(service.State.Snapshot is null, "Changing city immediately clears old weather");
            release.SetResult();
            await Until(() => service.State.Snapshot?.City == "Tokyo");
            Check(!wrongCityShown && service.State.City == "Tokyo", "In-flight old-city result cannot overwrite current city");
        }

        var attempts = 0;
        using var timeoutHttp = new HttpClient(new FakeHandler((request, token) =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new TaskCanceledException("HTTP timeout");
            return Task.FromResult(Response(request));
        }));
        await using (var service = new WeatherFetchService(options with
        {
            RetryInterval = TimeSpan.FromMilliseconds(40)
        }, timeoutHttp))
        {
            service.Start();
            await Until(() => service.State.Snapshot is not null);
            Check(attempts >= 3 && service.State.LastError is null, "HTTP timeout retries rather than stopping worker");
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blockedHttp = new HttpClient(new FakeHandler(async (request, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Response(request);
        }));
        var blocked = new WeatherFetchService(options, blockedHttp);
        blocked.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await blocked.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        Check(true, "Disposal cancels an active HTTP request promptly");

        Check(!Directory.EnumerateFileSystemEntries(Root).Any(), "Weather tests do not create files or directories");
    }

    private static void PanelTests(string? outputDirectory)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var bindingTrace = new StringWriter();
        var listener = new TextWriterTraceListener(bindingTrace);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        using var service = new WeatherFetchService();
        var window = new StyleCheckWindow(service);
        var dark = (ClockPanel)window.FindName("DarkPanel");
        var light = (ClockPanel)window.FindName("LightPanel");
        var sample = new WeatherSnapshot("Test", "Test location", 1, 1, -12, -14, 80, 20, 73, true, DateTimeOffset.UtcNow.AddHours(-1));
        dark.PreviewWeather = light.PreviewWeather = new("Test", sample);
        dark.TimeZoneId = "UTC";
        dark.ShowSeconds = true;
        Check(dark.Display.Zone == "UTC+00:00" && dark.Display.Time.Length == 8, "Panel applies time zone and seconds");
        Check(dark.Display.Temperature == "-12°C" && dark.Display.StaleVisibility == Visibility.Visible, "Panel displays negative temperature and stale indicator");
        dark.TimeZoneId = "invalid-zone";
        Check(dark.Display.Tooltip.Contains("无效时区"), "Invalid time zone falls back visibly to local time");
        dark.TimeZoneId = "Pacific Standard Time";
        dark.ShowWeather = false;
        Check(dark.Display.WeatherVisibility == Visibility.Collapsed, "Weather visibility can be controlled by host");
        dark.ShowWeather = true;
        dark.PreviewWeather = new("Test", LastError: "offline");
        Check(dark.Display.Temperature == "--" && dark.Display.Tooltip.Contains("offline"), "Missing weather shows placeholder and error");
        ((ComboBox)window.FindName("WeatherMode")).SelectedIndex = 1;
        ((CheckBox)window.FindName("SecondsToggle")).IsChecked = true;
        var content = (UIElement)window.Content;
        window.Content = null;
        var renderRoot = new Border { Background = window.Background, Child = content };
        renderRoot.Measure(new Size(920, 660));
        renderRoot.Arrange(new Rect(0, 0, 920, 660));
        renderRoot.UpdateLayout();
        // Render without opening a native desktop window.
        if (outputDirectory is not null)
        {
            Directory.CreateDirectory(outputDirectory);
            Render(renderRoot, Path.Combine(outputDirectory, "style-check.png"), 920, 660);
            ((ComboBox)window.FindName("ScalePicker")).SelectedIndex = 3;
            ((ComboBox)window.FindName("WeatherMode")).SelectedIndex = 3;
            renderRoot.Measure(new Size(920, 660));
            renderRoot.Arrange(new Rect(0, 0, 920, 660));
            renderRoot.UpdateLayout();
            Render(renderRoot, Path.Combine(outputDirectory, "style-check-200.png"), 920, 660);
        }
        listener.Flush();
        Check(!bindingTrace.ToString().Contains("Error"), "Style Check renders without WPF binding errors");
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        window.Close();
        app.Shutdown();
    }

    private static void IntegrationTests()
    {
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        var before = ClockFormatter.Format(new DateTimeOffset(2026, 9, 30, 15, 59, 59, TimeSpan.Zero),
            "Singapore Standard Time", true, true, new("Hangzhou", LastError: "offline"), culture);
        var after = ClockFormatter.Format(new DateTimeOffset(2026, 9, 30, 16, 0, 0, TimeSpan.Zero),
            "Singapore Standard Time", true, true, new("Hangzhou"), culture);
        Check(before.Time == "23:59:59" && before.Date.StartsWith("09/30") && after.Time == "00:00:00" &&
            after.Date.StartsWith("10/01"), "Taskbar and preview share target-zone midnight rollover");
        Check(before.Tooltip.Contains("Hangzhou") && before.Tooltip.Contains("offline") && before.Zone == "UTC+08:00",
            "Weather city stays independent of clock time zone");
        var winter = ClockFormatter.Format(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            "Pacific Standard Time", false, false, null);
        var summer = ClockFormatter.Format(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            "Pacific Standard Time", false, false, null);
        Check(winter.Zone == "UTC-08:00" && summer.Zone == "UTC-07:00", "Daylight saving offsets follow date");
        var path = Path.Combine(Root, "settings.json");
        var settings = new AppSettings { TimeZoneId = "UTC", City = "Tokyo", Width = 240, Enabled = false, ShowSeconds = true };
        settings.Save(path);
        Check(AppSettings.Load(path) == settings, "Product settings survive restart including disabled state");
        File.WriteAllText(path, "{ corrupt");
        Check(AppSettings.Load(path) == new AppSettings(), "Corrupt settings recover with safe defaults");
    }

    private static void Render(Visual visual, string path, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
    private static HttpResponseMessage Response(HttpRequestMessage request)
    {
        var json = request.RequestUri!.Host == "geocoding-api.open-meteo.com"
            ? """{"results":[{"name":"Test city","admin1":"Region","country":"Country","latitude":37.77,"longitude":-122.42}]}"""
            : """{"current":{"temperature_2m":22.4,"apparent_temperature":21,"relative_humidity_2m":65,"wind_speed_10m":12,"weather_code":2,"is_day":1}}""";
        return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Test condition not reached");
            await Task.Delay(10);
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
        _passed++;
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
