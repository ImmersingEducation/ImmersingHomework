using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class MainViewSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<MainViewSettingsPage>();

    public MainViewSettingsPage()
    {
        _logger.Debug("MainViewSettingsPage 初始化");
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("MainViewSettingsPage 附加到视觉树，初始化控件状态");
            Refresh();
        };
    }

    public void Refresh()
    {
        MainViewColumnNumericUpDown.Value = AppSettings.Instance.MainViewColumns.Value;
    }

    private void MainViewColumnNumericUpDown_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue is not { } newValue)
            return;

        var columns = Math.Clamp((int)Math.Round(newValue, MidpointRounding.AwayFromZero), 1, 6);
        if (AppSettings.Instance.MainViewColumns.Value == columns)
            return;

        _logger.Information("主界面列数变更: {Columns}", columns);
        AppSettings.Instance.MainViewColumns.Value = columns;

        // 输入框允许小数，这里把显示值同步为取整后的列数。
        if (MainViewColumnNumericUpDown.Value != columns)
            MainViewColumnNumericUpDown.Value = columns;
    }
}
