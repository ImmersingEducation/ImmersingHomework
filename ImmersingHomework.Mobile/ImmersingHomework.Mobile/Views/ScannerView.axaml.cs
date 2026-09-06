using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Shared.Services;

namespace ImmersingHomework.Mobile.Views;

public partial class ScannerView : UserControl
{
    public ScannerView()
    {
        InitializeComponent();
    }

    public event Action? BackRequested;

    public event Action<Homework>? HomeworkParsed;

    public event Action? ParseFailed;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MauiCameraScannerBridge.BarcodeDetected += OnBarcodeDetected;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        MauiCameraScannerBridge.BarcodeDetected -= OnBarcodeDetected;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }

    private void OnBarcodeDetected(object? sender, BarcodeDetectedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => TryHandleScan(e.Value));
    }

    private void TryHandleScan(string value)
    {
        Homework homework;
        try
        {
            homework = HomeworkQrCodeService.ParseQrCodeText(value);
        }
        catch
        {
            ParseFailed?.Invoke();
            return;
        }

        HomeworkParsed?.Invoke(homework);
    }
}
