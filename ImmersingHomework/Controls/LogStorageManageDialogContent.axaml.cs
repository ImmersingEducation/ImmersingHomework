using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;

namespace ImmersingHomework.Controls;

public partial class LogStorageManageDialogContent : UserControl
{
    public DateTimeOffset? SelectedDate { get; private set; }

    public LogStorageManageDialogContent()
    {
        InitializeComponent();
    }

    private void CalendarDatePicker_OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        SelectedDate = CalendarDatePicker.SelectedDate;
    }

    public void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        if (SelectedDate is null)
        {
            args.Cancel = true;
            return;
        }
    }
}
