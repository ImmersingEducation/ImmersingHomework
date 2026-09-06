using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ImmersingHomework.Mobile.Views;

public partial class FailureView : UserControl
{
    public FailureView()
    {
        InitializeComponent();
    }

    public event Action? RescanRequested;

    public event Action? BackRequested;

    private void OnRescanClicked(object? sender, RoutedEventArgs e)
    {
        RescanRequested?.Invoke();
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }
}
