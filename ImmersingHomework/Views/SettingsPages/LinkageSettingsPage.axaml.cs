using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class LinkageSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<LinkageSettingsPage>();

    private bool _flag = false;
    
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
        ClassIslandSwitch.IsChecked = AppSettings.Instance.EnableClassIslandIPCService.Value;
        TakeoverSubjectsSwitch.IsChecked = AppSettings.Instance.ClassIslandTakeoverSubjects.Value;
        RemindHomeworkAssignmentSwitch.IsChecked  = AppSettings.Instance.ShowHomeworkAfterSchool.Value;
        ShowHomeworkAfterSchoolSwitch.IsChecked = AppSettings.Instance.ShowHomeworkAfterSchool.Value;
        AfterSchoolWaitSecondCombobox.Text = AppSettings.Instance.AfterSchoolShowMainWindowWaitSecond.Value.ToString();
        ShowHomeworkBeforeFirstClassNextDaySwitch.IsChecked = AppSettings.Instance.ShowHomeworkBeforeFirstClassNextDay.Value;
        _flag = true;
    }
    
    private async void ClassIslandSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (ClassIslandSwitch.IsChecked.HasValue)
        {
            _logger.Information("ClassIsland 联动设置变更: {Value}", ClassIslandSwitch.IsChecked.Value);
            AppSettings.Instance.EnableClassIslandIPCService.Value = ClassIslandSwitch.IsChecked.Value;

            if (!_flag) return;
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window is null) return;
            var dialog = new FAContentDialog
            {
                Title = "重启软件",
                Content = "该设置需要重启生效。",
                PrimaryButtonText = "重启",
                CloseButtonText = "取消"
            };
            var result = await dialog.ShowAsync(window);
            if (result is FAContentDialogResult.Primary)
                ((App)Application.Current!).RestartApplication();
        }
    }
    
    private void TakeoverSubjectsSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (TakeoverSubjectsSwitch.IsChecked.HasValue)
        {
            _logger.Information("接管科目设置变更: {Value}", TakeoverSubjectsSwitch.IsChecked.Value);
            AppSettings.Instance.ClassIslandTakeoverSubjects.Value = TakeoverSubjectsSwitch.IsChecked.Value;
        }
    }

    private void ShowHomeworkAfterSchoolSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (ShowHomeworkAfterSchoolSwitch.IsChecked.HasValue)
        {
            _logger.Information("放学后显示作业变更: {Value}", ShowHomeworkAfterSchoolSwitch.IsChecked.Value);
            AppSettings.Instance.ShowHomeworkAfterSchool.Value = ShowHomeworkAfterSchoolSwitch.IsChecked.Value;
        }
    }

    private void AfterSchoolWaitSecondCombobox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _logger.Debug("放学后显示主界面等待时间选择变更事件触发");
        try
        {
            var seconds = Convert.ToInt32(AfterSchoolWaitSecondCombobox.Text);
            if (seconds <= 0) throw new OverflowException();
            _logger.Information("放学后显示主界面等待时间变更: {Seconds} 秒", seconds);
            AppSettings.Instance.AfterSchoolShowMainWindowWaitSecond.Value = seconds;
        }
        catch (Exception)
        {
            _logger.Debug("放学后显示主界面等待时间无效，重置为默认值");
            AfterSchoolWaitSecondCombobox.Text = "120";
        }
    }

    private void ShowHomeworkBeforeFirstClassNextDaySwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (ShowHomeworkBeforeFirstClassNextDaySwitch.IsChecked.HasValue)
        {
            _logger.Information("次日第一节课前显示作业变更: {Value}", ShowHomeworkBeforeFirstClassNextDaySwitch.IsChecked.Value);
            AppSettings.Instance.ShowHomeworkBeforeFirstClassNextDay.Value = ShowHomeworkBeforeFirstClassNextDaySwitch.IsChecked.Value;
        }
    }

    private void RemindHomeworkAssignmentSwitch_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (RemindHomeworkAssignmentSwitch.IsChecked.HasValue)
        {
            _logger.Information("课后提醒布置作业设置项变更：{Value}", RemindHomeworkAssignmentSwitch.IsChecked.Value);
            AppSettings.Instance.RemindHomeworkAssignmentAfterClass.Value = RemindHomeworkAssignmentSwitch.IsChecked.Value;
        }
    }
}