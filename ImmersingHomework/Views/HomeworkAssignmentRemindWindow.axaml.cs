using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Windowing;
using ImmersingHomework.Abstractions;

namespace ImmersingHomework.Views;

public partial class HomeworkAssignmentRemindWindow : FAAppWindow
{
    private readonly IClassIslandService _classIslandService;

    public HomeworkAssignmentRemindWindow(IClassIslandService classIslandService)
    {
        _classIslandService = classIslandService;
        InitializeComponent();
        DetailTextBlock.Text = $"请将 {_classIslandService.GetPreviousClassSubject()?.Name ?? ""} 作业布置于 方圆作业板。";
    }

    private void OpenButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ((App)Application.Current!).ShowMainWindow();
        Close();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}