using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Controls;
using ImmersingHomework.Services;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class StorageSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<StorageSettingsPage>();
    private readonly HomeworkStorageService _homeworkStorageService = new();
    private readonly OutputStorageService _outputStorageService = new();
    private readonly LogStorageService _logStorageService = new();
    private readonly SnapshotStorageService _snapshotStorageService = new();

    public StorageSettingsPage()
    {
        InitializeComponent();
        RefreshOccupancyStats();
    }

    private void RefreshOccupancyStats()
    {
        var homeworkDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Homeworks");
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
        var logDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");

        var snapshotSize = _snapshotStorageService.GetStorageUsage();
        var homeworkSize = GetDirectorySize(homeworkDir) - snapshotSize;
        var outputSize = GetDirectorySize(outputDir);
        var logSize = GetDirectorySize(logDir);
        var totalSize = homeworkSize + outputSize + logSize + snapshotSize;

        HomeworkOccupationTextBlock.Text = FormatSize(homeworkSize);
        OutputOccupationTextBlock.Text = FormatSize(outputSize);
        LogOccupationTextBlock.Text = FormatSize(logSize);
        SnapshotOccupationTextBlock.Text = FormatSize(snapshotSize);
        TotalOccupationTextBlock.Text = FormatSize(totalSize);
    }

    private void HomeworkManageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var control = new HomeworkStorageManageDialogContent();
        var dialog = new FAContentDialog()
        {
            Title = "管理作业数据",
            Content = control,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };
        dialog.PrimaryButtonClick += (s, args) => control.OnPrimaryButtonClick(args);
        dialog.PrimaryButtonClick += async (s, args) =>
        {
            if (control.SelectedDate is null)
                return;

            var deleted = _homeworkStorageService.DeleteBeforeAndEmpty(control.SelectedDate.Value);
            _logger.Information("删除作业文件完成，共删除 {Count} 个文件", deleted);
            RefreshOccupancyStats();
        };
        _ = dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void OutputManageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var control = new OutputStorageManageDialogContent();
        var dialog = new FAContentDialog()
        {
            Title = "管理导出产物",
            Content = control,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };
        dialog.PrimaryButtonClick += (s, args) => control.OnPrimaryButtonClick(args);
        dialog.PrimaryButtonClick += (s, args) =>
        {
            if (control.SelectedDate is null)
                return;

            var deletedCount = _outputStorageService.DeleteBefore(control.SelectedDate.Value);
            _logger.Information("删除导出产物完成，共删除 {Count} 个文件", deletedCount);
            RefreshOccupancyStats();
        };
        _ = dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void LogManageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var control = new LogStorageManageDialogContent();
        var dialog = new FAContentDialog()
        {
            Title = "管理日志",
            Content = control,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };
        dialog.PrimaryButtonClick += (s, args) => control.OnPrimaryButtonClick(args);
        dialog.PrimaryButtonClick += (s, args) =>
        {
            if (control.SelectedDate is null)
                return;

            var deletedCount = _logStorageService.DeleteBefore(control.SelectedDate.Value);
            _logger.Information("删除日志文件完成，共删除 {Count} 个文件", deletedCount);
            RefreshOccupancyStats();
        };
        _ = dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void SnapshotManageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var control = new SnapshotStorageManageDialogContent();
        var dialog = new FAContentDialog()
        {
            Title = "管理作业快照",
            Content = control,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };
        dialog.PrimaryButtonClick += (s, args) => control.OnPrimaryButtonClick(args);
        dialog.PrimaryButtonClick += (s, args) =>
        {
            if (control.SelectedDate is null)
                return;

            var deletedCount = _snapshotStorageService.ClearBefore(control.SelectedDate.Value);
            _logger.Information("删除作业快照完成，共删除 {Count} 个文件", deletedCount);
            RefreshOccupancyStats();
        };
        _ = dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        long size = 0;
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                size += new FileInfo(file).Length;
            }
            catch
            {
            }
        }
        return size;
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }
}
