using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Enums;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class BasicSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<BasicSettingsPage>();
    public BasicSettingsPage()
    {
        _logger.Debug("BasicSettingsPage 初始化");
        InitializeComponent();
        // macOS 下 URL 协议由 Info.plist 声明，不支持运行时注册/注销，隐藏该设置项
        UrlSchemaRegisteredExpander.IsVisible = !OperatingSystem.IsMacOS();
        this.AttachedToVisualTree += (_, _) => 
        {
            _logger.Debug("BasicSettingsPage 附加到视觉树，初始化控件状态");
            Refresh();
        };
    }

    public void Refresh()
    {
        LaunchAtStartupSwitch.IsChecked = AppSettings.Instance.LaunchAtStartup.Value;
        UrlSchemaRegisteredSwitch.IsChecked = AppSettings.Instance.UrlSchemaRegistered.Value;
        ThemeModeComboBox.SelectedIndex = Convert.ToInt32(AppSettings.Instance.ThemeMode.Value);
    }

    private void LaunchAtStartupSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (LaunchAtStartupSwitch.IsChecked.HasValue)
        {
            _logger.Information("开机自启动设置变更: {Value}", LaunchAtStartupSwitch.IsChecked.Value);
            AppSettings.Instance.LaunchAtStartup.Value = LaunchAtStartupSwitch.IsChecked.Value;
        }
    }

    private void UrlSchemaRegisteredSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (UrlSchemaRegisteredSwitch.IsChecked.HasValue)
        {
            _logger.Information("URL 协议注册设置变更: {Value}", UrlSchemaRegisteredSwitch.IsChecked.Value);
            AppSettings.Instance.UrlSchemaRegistered.Value = UrlSchemaRegisteredSwitch.IsChecked.Value;
        }
    }

    private void ThemeModeComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeModeComboBox.SelectedIndex >= 0)
        {
            var mode = (ThemeMode)ThemeModeComboBox.SelectedIndex;
            _logger.Information("外观样式设置变更: {Mode}", mode);
            AppSettings.Instance.ThemeMode.Value = mode;
        }
    }
}