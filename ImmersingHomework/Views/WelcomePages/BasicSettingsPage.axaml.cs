using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
        Refresh();
    }

    private void Refresh()
    {
        LaunchAtStartupSwitch.IsChecked = AppSettings.Instance.LaunchAtStartup.Value;
        UrlSchemaRegisteredSwitch.IsChecked = AppSettings.Instance.UrlSchemaRegistered.Value;
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
}