#pragma once
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Markup.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include "protocol.h"

namespace roam_clock {
struct ClockView {
    winrt::Windows::UI::Xaml::Controls::Grid grid{nullptr};
    winrt::Windows::UI::Xaml::Controls::TextBlock time{nullptr}, date{nullptr}, temperature{nullptr};
    winrt::Windows::UI::Xaml::Controls::StackPanel weather{nullptr};
    winrt::Windows::UI::Xaml::Controls::Canvas canvas{nullptr};
    winrt::Windows::UI::Xaml::Shapes::Path icon{nullptr};
    winrt::Windows::UI::Xaml::Shapes::Ellipse stale{nullptr};
    std::wstring lastPath;
    ClockView() {
        using namespace winrt::Windows::UI::Xaml;
        grid = Markup::XamlReader::Load(LR"(<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Margin="10,3" IsHitTestVisible="False"
            VerticalAlignment="Center">
            <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
            <TextBlock x:Name="TimeText" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center" HorizontalAlignment="Right" MinWidth="100" TextAlignment="Right"/>
            <StackPanel x:Name="Weather" Grid.RowSpan="2" Orientation="Horizontal" HorizontalAlignment="Left" VerticalAlignment="Center">
                <Viewbox Width="16" Height="16" VerticalAlignment="Center" Margin="0,0,5,0"><Canvas x:Name="WeatherCanvas" Width="26" Height="26">
                    <Path x:Name="WeatherIcon" Stroke="{ThemeResource SystemControlForegroundBaseHighBrush}" StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
                </Canvas></Viewbox>
                <TextBlock x:Name="TemperatureText" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center"/>
                <Ellipse x:Name="Stale" Width="4" Height="4" Margin="4,0,0,0" Fill="#E6B96A" VerticalAlignment="Center"/>
            </StackPanel>
            <TextBlock x:Name="DateText" Grid.Row="1" FontSize="11" Opacity="0.78" VerticalAlignment="Center" HorizontalAlignment="Right" MinWidth="100" TextAlignment="Right"/>
        </Grid>)").as<Controls::Grid>();
        time = grid.FindName(L"TimeText").as<Controls::TextBlock>();
        date = grid.FindName(L"DateText").as<Controls::TextBlock>();
        temperature = grid.FindName(L"TemperatureText").as<Controls::TextBlock>();
        weather = grid.FindName(L"Weather").as<Controls::StackPanel>();
        canvas = grid.FindName(L"WeatherCanvas").as<Controls::Canvas>();
        icon = grid.FindName(L"WeatherIcon").as<Shapes::Path>();
        stale = grid.FindName(L"Stale").as<Shapes::Ellipse>();
        if (!time || !date || !temperature || !weather || !canvas || !icon || !stale)
            winrt::throw_hresult(E_UNEXPECTED);
    }
    void Apply(SharedState const& state) {
        using namespace winrt::Windows::UI::Xaml;
        time.Text(state.time); date.Text(state.date); temperature.Text(state.temperature);
        weather.Visibility(state.flags & 1 ? Visibility::Visible : Visibility::Collapsed);
        stale.Visibility(state.flags & 2 ? Visibility::Visible : Visibility::Collapsed);
        if (lastPath != state.weatherPath) {
            std::wstring markup = L"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='";
            // Reject XML delimiters. Only geometry syntax supplied by the controller is accepted.
            std::wstring path = state.weatherPath;
            if (path.find_first_of(L"'<>\"&") != std::wstring::npos) winrt::throw_hresult(E_INVALIDARG);
            auto parsed = Markup::XamlReader::Load(markup + path + L"'/>").as<Shapes::Path>();
            // Parent() is null until a newly parsed tree has joined the live visual tree.
            // Keep its canvas directly so construction and reattachment are both safe.
            uint32_t index{};
            if (!canvas.Children().IndexOf(icon, index)) winrt::throw_hresult(E_UNEXPECTED);
            parsed.Stroke(icon.Stroke());
            parsed.StrokeThickness(1.5);
            parsed.StrokeStartLineCap(Media::PenLineCap::Round);
            parsed.StrokeEndLineCap(Media::PenLineCap::Round);
            parsed.StrokeLineJoin(Media::PenLineJoin::Round);
            canvas.Children().RemoveAt(index);
            canvas.Children().InsertAt(index, parsed);
            icon = parsed;
            lastPath = path;
        }
    }
};
}
