using Avalonia.Controls;
using Avalonia.Interactivity;
using ImmersingHomework.Shared.Models;

namespace ImmersingHomework.Mobile.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnScanClicked(object? sender, RoutedEventArgs e)
    {
        ShowScanner();
    }

    private void ShowScanner()
    {
        var scanner = new ScannerView();
        scanner.BackRequested += ShowHome;
        scanner.HomeworkParsed += OnHomeworkParsed;
        scanner.ParseFailed += OnParseFailed;
        RootContent.Content = scanner;
    }

    private void OnHomeworkParsed(Homework homework)
    {
        var export = new ExportView(homework);
        export.BackRequested += ShowHome;
        RootContent.Content = export;
    }

    private void OnParseFailed()
    {
        var failure = new FailureView();
        failure.RescanRequested += ShowScanner;
        failure.BackRequested += ShowHome;
        RootContent.Content = failure;
    }

    private void ShowHome()
    {
        RootContent.Content = HomePanel;
    }
}
