using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Services;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class BackupSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<BackupSettingsPage>();

    public BackupSettingsPage()
    {
        InitializeComponent();
    }

    private async void BackupButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击了开始备份按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
            return;

        var dialog = new FAContentDialog()
        {
            Title = "开始备份",
            Content = "请选择备份格式：",
            PrimaryButtonText = "软件私有格式",
            SecondaryButtonText = "UAF 备份",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(window);
        if (result is not (FAContentDialogResult.Primary or FAContentDialogResult.Secondary))
        {
            _logger.Debug("用户取消备份");
            return;
        }

        var homeworkPaths = GetAllHomeworkPaths();
        _logger.Information("开始备份，共 {Count} 个作业文件", homeworkPaths.Count);

        string targetDir;
        if (result == FAContentDialogResult.Primary)
        {
            var packagePath = BackupService.PackHomeworks(homeworkPaths);
            targetDir = Path.GetDirectoryName(packagePath) ?? GetBackupsDir();
        }
        else
        {
            BackupService.PackHomeworksAsUaf(homeworkPaths);
            targetDir = GetUafBackupsDir();
        }

        await ShowBackupCompletedDialog(window, targetDir);
    }

    private async void RestoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击了开始恢复按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
            return;

        var dialog = new FAContentDialog()
        {
            Title = "开始恢复",
            Content = "请选择恢复模式：",
            PrimaryButtonText = "软件私有格式",
            SecondaryButtonText = "UAF 备份",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(window);
        if (result is not (FAContentDialogResult.Primary or FAContentDialogResult.Secondary))
        {
            _logger.Debug("用户取消恢复");
            return;
        }

        var isPrivate = result == FAContentDialogResult.Primary;
        var filePaths = await PickFilesAsync(window, isPrivate);
        if (filePaths.Count == 0)
        {
            _logger.Information("用户未选择任何文件，取消恢复");
            return;
        }

        _logger.Information("开始恢复，共 {Count} 个文件", filePaths.Count);
        var restoredCount = isPrivate
            ? BackupService.UnpackHomeworks(filePaths[0]).Count
            : BackupService.UnpackHomeworksAsUaf(filePaths).Count;

        var successDialog = new FAContentDialog()
        {
            Title = "恢复完成",
            Content = $"成功恢复 {restoredCount} 个作业。",
            CloseButtonText = "关闭"
        };
        successDialog.CloseButtonClick += (_, _) => successDialog.Hide();
        await successDialog.ShowAsync(window);
    }

    private async Task ShowBackupCompletedDialog(Window window, string targetDir)
    {
        var fullPath = Path.GetFullPath(targetDir);
        var dialog = new FAContentDialog()
        {
            Title = "备份完成",
            Content = $"备份已完成，文件已保存到 {fullPath}。",
            PrimaryButtonText = "打开文件夹",
            CloseButtonText = "关闭"
        };
        dialog.PrimaryButtonClick += (_, _) =>
        {
            OpenInFileManager(fullPath);
            dialog.Hide();
        };
        dialog.CloseButtonClick += (_, _) => dialog.Hide();
        await dialog.ShowAsync(window);
    }

    private async Task<List<string>> PickFilesAsync(Window window, bool isPrivate)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
            return [];

        var filter = isPrivate
            ? new FilePickerFileType("软件私有格式备份") { Patterns = ["*.zip"] }
            : new FilePickerFileType("UAF 备份") { Patterns = ["*.pdf"] };

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = isPrivate ? "选择备份文件" : "选择 UAF 备份文件",
            AllowMultiple = !isPrivate,
            FileTypeFilter = [filter]
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(path => path is not null)
            .Cast<string>()
            .ToList();
    }

    private static List<string> GetAllHomeworkPaths()
    {
        var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Homeworks");
        if (!Directory.Exists(dataDir))
            return [];

        return Directory.GetFiles(dataDir, "*.json")
            .Where(file => DateOnly.TryParse(Path.GetFileNameWithoutExtension(file), out _))
            .ToList();
    }

    private static string GetBackupsDir()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backups");
    }

    private static string GetUafBackupsDir()
    {
        return Path.Combine(GetBackupsDir(), "Uaf");
    }

    private void OpenInFileManager(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = path,
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = path,
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "打开文件夹失败: {Path}", path);
        }
    }
}
