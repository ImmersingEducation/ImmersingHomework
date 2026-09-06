using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;

namespace ImmersingHomework.Controls;

public partial class HomeworkStorageManageDialogContent : UserControl
{
    public DateTimeOffset? SelectedDate { get; private set; }

    public HomeworkStorageManageDialogContent()
    {
        InitializeComponent();
    }

    private void CalendarDatePicker_OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        SelectedDate = CalendarDatePicker.SelectedDate;
        WarningTextBlock.Text = string.Empty;

        if (SelectedDate is null)
            return;

        var today = DateTimeOffset.Now.Date;
        if ((today - SelectedDate.Value.Date).Days < 3)
            WarningTextBlock.Text = "保留的作业数据太少，请谨慎删除";
    }

    public void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        if (SelectedDate is null)
        {
            WarningTextBlock.Text = "请选择一个日期";
            args.Cancel = true;
            return;
        }
    }
}
