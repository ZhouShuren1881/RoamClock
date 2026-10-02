using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RoamClock.WeatherFetch;

namespace RoamClock.Components;

/// <summary>Transparent clock content, independent of its window/taskbar host.</summary>
public partial class ClockPanel : UserControl
{
    public static readonly DependencyProperty WeatherSourceProperty = DependencyProperty.Register(
        nameof(WeatherSource), typeof(WeatherFetchService), typeof(ClockPanel), new PropertyMetadata(null, SourceChanged));
    public static readonly DependencyProperty TimeZoneIdProperty = DependencyProperty.Register(
        nameof(TimeZoneId), typeof(string), typeof(ClockPanel), new PropertyMetadata("Singapore Standard Time", ZoneChanged));
    public static readonly DependencyProperty ShowSecondsProperty = DependencyProperty.Register(
        nameof(ShowSeconds), typeof(bool), typeof(ClockPanel), new PropertyMetadata(false, DisplayChanged));
    public static readonly DependencyProperty ShowWeatherProperty = DependencyProperty.Register(
        nameof(ShowWeather), typeof(bool), typeof(ClockPanel), new PropertyMetadata(true, DisplayChanged));
    public static readonly DependencyProperty PreviewWeatherProperty = DependencyProperty.Register(
        nameof(PreviewWeather), typeof(WeatherFetchState), typeof(ClockPanel), new PropertyMetadata(null, DisplayChanged));

    private readonly DispatcherTimer _timer;
    private WeatherFetchService? _subscribedSource;

    public ClockPanel()
    {
        Display = new ClockPanelDisplay();
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateDisplay();
        Loaded += (_, _) => { Subscribe(); UpdateDisplay(); _timer.Start(); };
        Unloaded += (_, _) => { _timer.Stop(); Unsubscribe(); };
        UpdateDisplay();
    }

    public ClockPanelDisplay Display { get; }
    public WeatherFetchService? WeatherSource
    {
        get => (WeatherFetchService?)GetValue(WeatherSourceProperty);
        set => SetValue(WeatherSourceProperty, value);
    }
    public string TimeZoneId { get => (string)GetValue(TimeZoneIdProperty); set => SetValue(TimeZoneIdProperty, value); }
    public bool ShowSeconds { get => (bool)GetValue(ShowSecondsProperty); set => SetValue(ShowSecondsProperty, value); }
    public bool ShowWeather { get => (bool)GetValue(ShowWeatherProperty); set => SetValue(ShowWeatherProperty, value); }
    /// <summary>Optional design fixture; null restores live weather without changing its state.</summary>
    public WeatherFetchState? PreviewWeather
    {
        get => (WeatherFetchState?)GetValue(PreviewWeatherProperty);
        set => SetValue(PreviewWeatherProperty, value);
    }

    private static void SourceChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var panel = (ClockPanel)obj;
        panel.Unsubscribe();
        if (panel.IsLoaded) panel.Subscribe();
        panel.UpdateDisplay();
    }
    private static void ZoneChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var panel = (ClockPanel)obj;
        panel.UpdateDisplay();
    }
    private static void DisplayChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args) => ((ClockPanel)obj).UpdateDisplay();

    private void Subscribe()
    {
        _subscribedSource = WeatherSource;
        if (_subscribedSource is not null) _subscribedSource.StateChanged += WeatherChanged;
    }
    private void Unsubscribe()
    {
        if (_subscribedSource is not null) _subscribedSource.StateChanged -= WeatherChanged;
        _subscribedSource = null;
    }
    private void WeatherChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (IsLoaded) UpdateDisplay(); }));
    }

    private void UpdateDisplay()
    {
        if (Display is null) return;
        var content = ClockFormatter.Format(DateTimeOffset.UtcNow, TimeZoneId, ShowSeconds, ShowWeather,
            PreviewWeather ?? WeatherSource?.State);
        Display.Time = content.Time;
        Display.Date = content.Date;
        Display.Zone = content.Zone;
        Display.Temperature = content.Temperature;
        Display.WeatherPath = content.WeatherPath;
        Display.WeatherVisibility = content.ShowWeather ? Visibility.Visible : Visibility.Collapsed;
        Display.StaleVisibility = content.IsStale ? Visibility.Visible : Visibility.Collapsed;
        Display.Tooltip = content.Tooltip;
        Display.AccessibleText = content.Tooltip.Replace('\n', ' ');
    }
}
public sealed class ClockPanelDisplay : INotifyPropertyChanged
{
    private string _time = "", _date = "", _zone = "", _temperature = "", _weatherPath = "", _tooltip = "", _accessibleText = "";
    private Visibility _weatherVisibility, _staleVisibility;
    public string Time { get => _time; internal set => Set(ref _time, value); }
    public string Date { get => _date; internal set => Set(ref _date, value); }
    public string Zone { get => _zone; internal set => Set(ref _zone, value); }
    public string Temperature { get => _temperature; internal set => Set(ref _temperature, value); }
    public string WeatherPath { get => _weatherPath; internal set => Set(ref _weatherPath, value); }
    public string Tooltip { get => _tooltip; internal set => Set(ref _tooltip, value); }
    public string AccessibleText { get => _accessibleText; internal set => Set(ref _accessibleText, value); }
    public Visibility WeatherVisibility { get => _weatherVisibility; internal set => Set(ref _weatherVisibility, value); }
    public Visibility StaleVisibility { get => _staleVisibility; internal set => Set(ref _staleVisibility, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(property));
    }
}
