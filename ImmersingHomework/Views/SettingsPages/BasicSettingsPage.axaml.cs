using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Color = Avalonia.Media.Color;
using ImmersingHomework.Enums;
using ImmersingHomework.Helper;
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
        FloatingWindowSideComboBox.SelectedIndex = (int)AppSettings.Instance.FloatingWindowLeftOrRight.Value;

        // 桌面驻留模式下不显示浮窗，浮窗位置设置自然失效。
        var isDesktopResident = AppSettings.Instance.MainViewMode.Value == MainViewMode.DesktopResident;
        FloatingWindowSideExpander.IsEnabled = !isDesktopResident;
        FloatingWindowSideExpander.Description = isDesktopResident
            ? "桌面驻留模式下不显示浮窗，此设置不可用。"
            : "选择浮窗显示在屏幕左侧还是右侧。";

        ThemeModeComboBox.SelectedIndex = Convert.ToInt32(AppSettings.Instance.ThemeMode.Value);
        RefreshThemeAccentColor();
        TeachingSecurityModeComboBox.SelectedIndex = Convert.ToInt32(AppSettings.Instance.TeachingSecurityMode.Value);
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

    private void ThemeModeComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemeModeComboBox.SelectedIndex >= 0)
        {
            var mode = (ThemeMode)ThemeModeComboBox.SelectedIndex;
            _logger.Information("外观样式设置变更: {Mode}", mode);
            AppSettings.Instance.ThemeMode.Value = mode;
        }
    }

    private void ThemeAccentColorPicker_OnColorChanged(object? sender, ColorChangedEventArgs e)
    {
        ApplyAccentColorPreview(e.NewColor);

        // 拖动取色时会连续触发；变色由 App 订阅 ThemeAccentColor 完成，写盘则合并为一次
        var value = ThemeAccentHelper.ToSettingValue(e.NewColor);
        if (AppSettings.Instance.ThemeAccentColor.Value == value)
            return;

        _logger.Information("主题色设置变更: {Value}", value);
        AppSettings.Instance.ThemeAccentColor.Value = value;
    }

    private void RefreshThemeAccentColor()
    {
        // 未设置过主题色时，把当前生效的强调色（系统强调色）显示出来，用户在此基础上修改即可
        var color = ThemeAccentHelper.Parse(AppSettings.Instance.ThemeAccentColor.Value)
                    ?? ThemeAccentHelper.GetCurrentAccentColor()
                    ?? Colors.DodgerBlue;
        ApplyAccentColorPreview(color);

        // 直接写 Color 属性；上面的一致性判断会把这次刷新触发的回写拦下来
        ThemeAccentColorPicker.Color = color;
    }

    private void ApplyAccentColorPreview(Color color)
    {
        ThemeAccentColorPreview.Background = new SolidColorBrush(color);
    }

    private void TeachingSecurityModeComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)    {
        if (TeachingSecurityModeComboBox.SelectedIndex >= 0)
        {
            var mode = (TeachingSecurityMode)TeachingSecurityModeComboBox.SelectedIndex;
            _logger.Information("教学安全模式设置变更: {Mode}", mode);
            AppSettings.Instance.TeachingSecurityMode.Value = mode;
        }
    }
}