using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Services;
using ImmersingHomework.Shared.Models;

namespace ImmersingHomework.Mobile.Views;

public partial class ExportView : UserControl
{
    private readonly Homework _homework;

    public ExportView(Homework homework)
    {
        InitializeComponent();
        _homework = homework;
        HomeworkDateTextBlock.Text = $"作业日期：{homework.Date:yyyy-MM-dd}";
    }

    public event Action? BackRequested;

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }

    private async void OnExportImageClicked(object? sender, RoutedEventArgs e)
    {
        await ExportAsync(isImage: true);
    }

    private async void OnExportPdfClicked(object? sender, RoutedEventArgs e)
    {
        await ExportAsync(isImage: false);
    }

    private async Task ExportAsync(bool isImage)
    {
        SetExportButtonsEnabled(false);
        StatusText.Text = "正在导出…";

        byte[] data;
        string extension;
        string mimeType;
        try
        {
            if (isImage)
            {
                data = await Task.Run(() => HomeworkImageService.HomeworkToImageBytes(_homework));
                extension = ".png";
                mimeType = "image/png";
            }
            else
            {
                data = await Task.Run(() =>
                {
                    var pdfService = new UafPdfService();
                    pdfService.InitializeFonts();
                    return pdfService.GeneratePdfFromHomework(_homework);
                });
                extension = ".pdf";
                mimeType = "application/pdf";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"导出失败：{ex.Message}";
            SetExportButtonsEnabled(true);
            return;
        }

        SavedExport saved;
        try
        {
            var fileName = $"{_homework.Date:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}{extension}";
            saved = ExportStorage.SaveFile(data, fileName, mimeType);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"保存失败：{ex.Message}";
            SetExportButtonsEnabled(true);
            return;
        }

        SetExportButtonsEnabled(true);
        StatusText.Text = $"已导出：{saved.DisplayPath}";
        await ShowExportResultDialog(saved, data, mimeType, isImage);
    }

    private async Task ShowExportResultDialog(SavedExport saved, byte[] data, string mimeType, bool isImage)
    {
        var dialog = new FAContentDialog
        {
            Title = "导出成功",
            Content = $"已保存到：{saved.DisplayPath}",
            PrimaryButtonText = "打开",
            SecondaryButtonText = "复制",
            CloseButtonText = "关闭"
        };

        dialog.PrimaryButtonClick += async (_, _) => await OpenFileAsync(saved, mimeType);
        dialog.SecondaryButtonClick += async (_, _) => await CopyAsync(saved, data, isImage);

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null)
            await dialog.ShowAsync(topLevel);
    }

    private async Task OpenFileAsync(SavedExport saved, string mimeType)
    {
        try
        {
            if (ExportStorage.OpenFile is not null)
            {
                ExportStorage.OpenFile(saved.Location, mimeType);
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not null)
                await topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(saved.Location));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"打开失败：{ex.Message}";
        }
    }

    private async Task CopyAsync(SavedExport saved, byte[] data, bool isImage)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;

            if (isImage)
                await clipboard.SetBitmapAsync(new Bitmap(new MemoryStream(data)));
            else
                await clipboard.SetTextAsync(saved.DisplayPath);

            StatusText.Text = "已复制。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"复制失败：{ex.Message}";
        }
    }

    private void SetExportButtonsEnabled(bool enabled)
    {
        ExportImageButton.IsEnabled = enabled;
        ExportPdfButton.IsEnabled = enabled;
    }
}
