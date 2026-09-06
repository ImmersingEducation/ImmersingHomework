using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Windowing;

namespace ImmersingHomework.Views;

public partial class InstanceAlreadyRunningWindow : FAAppWindow
{
    public InstanceAlreadyRunningWindow()
    {
        InitializeComponent();
    }

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
