using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using RoamClock.Adpater;
using RoamClock.Components;
using RoamClock.WeatherFetch;

namespace RoamClock;

public partial class App : Application
{
    private Mutex? _instance;
    private TrayIcon? _tray;
    private DispatcherTimer? _clockTimer;
    public WeatherFetchService WeatherService { get; private set; } = null!;
    public TaskbarAdapter? Adapter { get; private set; }
    public AppSettings Settings { get; private set; } = new();
    public string? AdapterError { get; private set; }
    public bool IsExiting { get; private set; }
    public bool CanHideToTray => _tray is not null;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture;
        if (e.Args.Contains("--style-check"))
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            WeatherService = new WeatherFetchService();
            WeatherService.Start();
            MainWindow = new StyleCheckWindow(WeatherService);
            MainWindow.Show();
            return;
        }
        _instance = new Mutex(true, @"Local\RoamClock.Product", out bool created);
        if (!created)
        {
            MessageBox.Show("漫游时钟已在运行，请从托盘打开设置。", "RoamClock");
            Shutdown();
            return;
        }
        Settings = AppSettings.Load();
        WeatherService = new WeatherFetchService(new WeatherFetchOptions { City = Settings.City });
        WeatherService.SetEnabled(Settings.ShowWeather);
        WeatherService.Start();
        try { Adapter = new TaskbarAdapter(); Adapter.SetEnabled(Settings.Enabled); }
        catch (Exception ex) { AdapterError = ex.Message; }
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => PublishClock();
        PublishClock();
        _clockTimer.Start();
        try { _tray = new TrayIcon(OpenSettings, () => _ = ExitAsync()); }
        catch (Exception ex) { AdapterError = "托盘不可用：" + ex.Message; }
        MainWindow = new MainWindow(this);
        if (!e.Args.Contains("--background") || _tray is null) MainWindow.Show();
    }
    public void ApplySettings(AppSettings settings)
    {
        settings.Save();
        Settings = settings;
        if (!settings.ShowWeather) WeatherService.SetEnabled(false);
        WeatherService.SetCity(settings.City);
        WeatherService.SetEnabled(settings.ShowWeather);
        Adapter?.SetEnabled(settings.Enabled);
        PublishClock();
    }
    private void PublishClock()
    {
        try
        {
            Adapter?.SetContent(ClockFormatter.Format(DateTimeOffset.UtcNow, Settings.TimeZoneId,
                Settings.ShowSeconds, Settings.ShowWeather, WeatherService.State), Settings.Width);
        }
        catch (Exception ex) { AdapterError = "同步时钟失败：" + ex.Message; }
    }
    private void OpenSettings()
    {
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }
    public async Task ExitAsync()
    {
        if (IsExiting) return;
        IsExiting = true;
        _clockTimer?.Stop();
        try
        {
            if (Adapter is not null) await Adapter.DisposeAsync();
            if (WeatherService is not null) await WeatherService.DisposeAsync();
        }
        catch (Exception ex) { AdapterError = "退出清理：" + ex.Message; }
        finally { WeatherService?.Dispose(); _tray?.Dispose(); _tray = null; Shutdown(); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _clockTimer?.Stop();
        // Shutdown/session exit can bypass ExitAsync. Adapter cleanup runs on
        // worker threads and does not need the WPF dispatcher.
        try { Adapter?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception ex) { AdapterError = "退出清理：" + ex.Message; }
        WeatherService?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
