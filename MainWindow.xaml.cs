using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RoamClock.Adpater;
using RoamClock.WeatherFetch;

namespace RoamClock;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        var zones = TimeZoneInfo.GetSystemTimeZones();
        ZonePicker.ItemsSource = zones;
        ZonePicker.SelectedItem = zones.FirstOrDefault(z => z.Id == app.Settings.TimeZoneId) ?? TimeZoneInfo.Local;
        CityInput.Text = app.Settings.City;
        SecondsToggle.IsChecked = app.Settings.ShowSeconds;
        WeatherToggle.IsChecked = app.Settings.ShowWeather;
        EnabledToggle.IsChecked = app.Settings.Enabled;
        WidthSlider.Value = app.Settings.Width;
        PreviewPanel.WeatherSource = app.WeatherService;
        RefreshPreview();
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        Closed += (_, _) => _statusTimer.Stop();
        Closing += HideToTray;
        UpdateStatus();
    }
    private void RefreshPreview()
    {
        PreviewPanel.TimeZoneId = _app.Settings.TimeZoneId;
        PreviewPanel.ShowSeconds = _app.Settings.ShowSeconds;
        PreviewPanel.ShowWeather = _app.Settings.ShowWeather;
        PreviewPanel.Width = _app.Settings.Width;
    }
    private void ApplySettings(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CityInput.Text)) { WeatherText.Text = "请输入天气城市。"; return; }
        try
        {
            _app.ApplySettings(new AppSettings
            {
                TimeZoneId = ((TimeZoneInfo)ZonePicker.SelectedItem).Id,
                City = CityInput.Text.Trim(), ShowSeconds = SecondsToggle.IsChecked == true,
                ShowWeather = WeatherToggle.IsChecked == true, Enabled = EnabledToggle.IsChecked == true,
                Width = (int)WidthSlider.Value
            });
            RefreshPreview();
            UpdateStatus();
        }
        catch (Exception ex) { WeatherText.Text = "保存设置失败：" + ex.Message; }
    }
    private void UpdateStatus()
    {
        var status = _app.Adapter?.Status;
        StatusText.Text = _app.AdapterError ?? (status?.Phase switch
        {
            AdapterPhase.Embedded => $"任务栏已连接 · {status.AttachedCount} 个时钟" +
                (status.AppliedRevision != status.RequestedRevision ? " · 正在同步显示" : ""),
            AdapterPhase.Disabled => "任务栏时钟已停用，系统时钟已恢复。",
            AdapterPhase.Unsupported => "当前任务栏时钟结构暂不支持，系统时钟保留。",
            AdapterPhase.Failed => status.Detail ?? $"接入失败：0x{status.HResult:X8}",
            AdapterPhase.Observing => "正在寻找系统时钟…",
            _ => "正在等待 Explorer 任务栏…"
        });
        var weather = _app.WeatherService.State;
        RefreshButton.IsEnabled = _app.Settings.ShowWeather && !weather.IsRefreshing;
        WeatherText.Text = !_app.Settings.ShowWeather ? "天气已隐藏，后台查询已暂停。" :
            weather.IsRefreshing ? $"正在更新 {weather.City} 天气…" :
            weather.LastError is not null ? $"{weather.City} · {weather.LastError}" :
            weather.Snapshot is not null ? $"{weather.Snapshot.Location} · {weather.Snapshot.Temperature:0}°C · 更新于 {weather.Snapshot.UpdatedAt.ToLocalTime():t} · Open-Meteo" :
            $"{weather.City} · 等待天气数据";
    }
    private void RefreshWeather(object sender, RoutedEventArgs e) => _app.WeatherService.RequestRefresh();
    private void OpenStyleCheck(object sender, RoutedEventArgs e)
    {
        var weather = new WeatherFetchService(new WeatherFetchOptions { City = _app.Settings.City });
        weather.Start();
        var window = new StyleCheckWindow(weather) { Owner = this };
        window.Closed += (_, _) => weather.Dispose();
        window.Show();
    }
    private async void ExitApplication(object sender, RoutedEventArgs e) => await _app.ExitAsync();
    private void HideToTray(object? sender, CancelEventArgs e)
    {
        if (_app.IsExiting) return;
        e.Cancel = true;
        if (_app.CanHideToTray) Hide();
        else _ = _app.ExitAsync();
    }
}
