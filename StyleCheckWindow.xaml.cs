using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RoamClock.WeatherFetch;

namespace RoamClock;

public partial class StyleCheckWindow : Window
{
    private readonly WeatherFetchService _weather;
    private bool _ready;
    public StyleCheckWindow(WeatherFetchService weather)
    {
        _weather = weather;
        InitializeComponent();
        ZonePicker.ItemsSource = new[]
        {
            new ZoneChoice("新加坡时间 · Singapore", "Singapore Standard Time"),
            new ZoneChoice("太平洋时间 · Los Angeles", "Pacific Standard Time"),
            new ZoneChoice("中国时间 · Shanghai", "China Standard Time"),
            new ZoneChoice("日本时间 · Tokyo", "Tokyo Standard Time"),
            new ZoneChoice("东部时间 · New York", "Eastern Standard Time"),
            new ZoneChoice("英国时间 · London", "GMT Standard Time"),
            new ZoneChoice("UTC", "UTC"),
            new ZoneChoice("系统本地时间", TimeZoneInfo.Local.Id)
        };
        ZonePicker.DisplayMemberPath = nameof(ZoneChoice.Label);
        ZonePicker.SelectedIndex = 0;
        CityInput.Text = weather.State.City;
        DarkPanel.WeatherSource = LightPanel.WeatherSource = weather;
        _ready = true;
        ApplySettings();
        Loaded += (_, _) => { _weather.StateChanged += WeatherChanged; UpdateStatus(); };
        Closed += (_, _) => _weather.StateChanged -= WeatherChanged;
    }
    private void SettingsChanged(object sender, RoutedEventArgs args) { if (_ready) ApplySettings(); }
    private void ApplySettings()
    {
        var zone = (ZoneChoice)ZonePicker.SelectedItem;
        var scale = double.Parse((string)((ComboBoxItem)ScalePicker.SelectedItem).Tag, CultureInfo.InvariantCulture);
        var preview = Preview((string)((ComboBoxItem)WeatherMode.SelectedItem).Tag);
        _weather.SetEnabled(WeatherToggle.IsChecked == true);
        foreach (var panel in new[] { DarkPanel, LightPanel })
        {
            panel.TimeZoneId = zone.Id;
            panel.ShowSeconds = SecondsToggle.IsChecked == true;
            panel.ShowWeather = WeatherToggle.IsChecked == true;
            panel.LayoutTransform = new ScaleTransform(scale, scale);
            panel.PreviewWeather = preview;
        }
        UpdateStatus();
    }
    private static WeatherFetchState? Preview(string mode)
    {
        if (mode == "live") return null;
        if (mode == "loading") return new("San Francisco", IsRefreshing: true);
        if (mode == "error") return new("San Francisco", LastError: "天气请求失败（预览示例）");
        var code = mode switch { "rain" => 63, "snow" => 73, _ => 0 };
        var temperature = mode switch { "snow" => -8, "rain" => 16, _ => 22 };
        var snapshot = new WeatherSnapshot("San Francisco", "San Francisco, California", 37.77, -122.42,
            temperature, temperature - 2, 65, 12, code, true,
            DateTimeOffset.UtcNow.AddMinutes(mode == "stale" ? -90 : 0));
        return new("San Francisco", snapshot, LastError: mode == "stale" ? "刷新失败；显示最近成功的天气（预览示例）" : null);
    }
    private void ApplyCity(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(CityInput.Text)) { StatusText.Text = "请输入城市名。"; return; }
        _weather.SetCity(CityInput.Text);
        WeatherMode.SelectedIndex = 0;
        UpdateStatus();
    }
    private void CityKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { ApplyCity(sender, args); args.Handled = true; }
    }
    private void RefreshWeather(object sender, RoutedEventArgs args) => _weather.RequestRefresh();
    private void WeatherChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished) Dispatcher.BeginInvoke(new Action(UpdateStatus));
    }
    private void UpdateStatus()
    {
        var state = _weather.State;
        RefreshButton.IsEnabled = _weather.IsEnabled && !state.IsRefreshing;
        var prefix = WeatherMode.SelectedIndex == 0 ? "" : "当前显示示例；后台实时天气：";
        StatusText.Text = !_weather.IsEnabled ? "天气已隐藏，后台查询已暂停。" : prefix + (state.IsRefreshing ? $"正在获取 {state.City} 的天气…" :
            state.LastError is not null ? $"{state.City} · {state.LastError}" :
            state.Snapshot is not null ? $"{state.Snapshot.Location} · {state.Snapshot.Temperature:0}°C · 更新于 {state.Snapshot.UpdatedAt.ToLocalTime():T}" :
            $"{state.City} · 等待天气数据");
    }
    private sealed record ZoneChoice(string Label, string Id);
}
