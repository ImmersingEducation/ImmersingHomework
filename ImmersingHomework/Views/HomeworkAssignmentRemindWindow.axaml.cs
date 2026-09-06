using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Windowing;
using ImmersingHomework.Services;

namespace ImmersingHomework.Views;

public partial class HomeworkAssignmentRemindWindow : FAAppWindow
{
    public HomeworkAssignmentRemindWindow()
    {
        InitializeComponent();
        DetailTextBlock.Text = $"请将 {ClassIslandService.Instance.GetPreviousClassSubject()?.Name ?? ""} 作业布置于 方圆作业板。";
    }

    private void OpenButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ((App)Application.Current!).ShowMainWindow();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}