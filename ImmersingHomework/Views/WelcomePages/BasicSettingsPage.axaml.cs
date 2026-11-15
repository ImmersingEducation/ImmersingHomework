using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Enums;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Views.WelcomePages;

public partial class BasicSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<BasicSettingsPage>();
    
    public BasicSettingsPage()
    {
        InitializeComponent();
        // macOS 下 URL 协议由 Info.plist 声明，不支持运行时注册/注销，隐藏该设置项
        UrlSchemaRegisteredExpander.IsVisible = !OperatingSystem.IsMacOS();
        Refresh();
    }

    private void Refresh()
    {
        LaunchAtStartupSwitch.IsChecked = AppSettings.Instance.LaunchAtStartup.Value;
        UrlSchemaRegisteredSwitch.IsChecked = AppSettings.Instance.UrlSchemaRegistered.Value;
        FloatingWindowSideComboBox.SelectedIndex = (int)AppSettings.Instance.FloatingWindowLeftOrRight.Value;
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

    private void FloatingWindowSideComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FloatingWindowSideComboBox.SelectedIndex < 0)
            return;

        var side = FloatingWindowSideComboBox.SelectedIndex == 0
            ? FloatingWindowSide.Left
            : FloatingWindowSide.Right;
        _logger.Information("浮窗位置设置变更: {Side}", side);
        AppSettings.Instance.FloatingWindowLeftOrRight.Value = side;
    }
}