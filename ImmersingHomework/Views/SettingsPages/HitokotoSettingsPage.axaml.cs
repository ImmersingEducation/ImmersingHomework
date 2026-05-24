using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class HitokotoSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HitokotoSettingsPage>();
    public HitokotoSettingsPage()
    {
        _logger.Debug("HitokotoSettingsPage 初始化");
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("HitokotoSettingsPage 附加到视觉树，初始化控件状态");
            Refresh();
        };
    }

    private void Refresh()
    {
        HitokotoDisplayModeComboBox.SelectedIndex = Convert.ToInt32(AppSettings.Instance.HitokotoDisplayMode.Value);
        HitokotoRefreshTimeSpanCombobox.Text = AppSettings.Instance.HitokotoRefreshTimeSpan.Value.ToString();
    }

    private void HitokotoDisplayModeComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (HitokotoDisplayModeComboBox.SelectedIndex >= 0)
        {
            var newMode = (HitokotoDisplayMode)HitokotoDisplayModeComboBox.SelectedIndex;
            _logger.Information("一言显示方式变更: {NewMode}", newMode);
            AppSettings.Instance.HitokotoDisplayMode.Value = newMode;
        }
    }

    private void HitokotoRefreshTimeSpanCombobox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _logger.Debug("一言刷新间隔选择变更事件触发");
        try
        {
            var span = Convert.ToInt32(HitokotoRefreshTimeSpanCombobox.Text);
            if (span <= 0) throw new OverflowException();
            _logger.Information("一言刷新间隔变更: {Span} 秒", span);
            AppSettings.Instance.HitokotoRefreshTimeSpan.Value = span;
        }
        catch (Exception)
        {
            _logger.Debug("一言刷新间隔无效，重置为默认值");
            HitokotoRefreshTimeSpanCombobox.SelectedIndex = 2;
        }
    }
}