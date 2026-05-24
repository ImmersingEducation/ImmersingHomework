using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class LinkageSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<LinkageSettingsPage>();
    public LinkageSettingsPage()
    {
        _logger.Debug("LinkageSettingsPage 初始化");
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) => 
        {
            _logger.Debug("LinkageSettingsPage 附加到视觉树，初始化控件状态");
            Refresh();
        };
    }

    public void Refresh()
    {
        // ClassIslandSwitch.IsChecked = AppSettings.Instance.EnableClassIslandIPCService.Value;
    }
    
    private void ClassIslandSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        // if (ClassIslandSwitch.IsChecked.HasValue)
        // {
        //     _logger.Information("ClassIsland 联动设置变更: {Value}", ClassIslandSwitch.IsChecked.Value);
        //     AppSettings.Instance.EnableClassIslandIPCService.Value = ClassIslandSwitch.IsChecked.Value;
        // }
    }
}