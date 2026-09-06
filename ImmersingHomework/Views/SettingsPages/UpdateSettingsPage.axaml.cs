using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ImmersingHomework.Enums;
using ImmersingHomework.Models;
using ImmersingHomework.Services;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class UpdateSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<UpdateSettingsPage>();
    private CheckUpdateResponse? _latestUpdate;

    public UpdateSettingsPage()
    {
        _logger.Debug("UpdateSettingsPage 初始化");
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("UpdateSettingsPage 附加到视觉树，初始化控件状态");
            Refresh();
        };
    }

    private void Refresh()
    {
        var currentVersion = UpdateService.GetCurrentVersion();
        UpdateStatusItem.Content = "尚未检查更新";
        UpdateStatusItem.Description = $"当前版本 {currentVersion}，点击“检查更新”获取最新版本。";
        UpdateChannelComboBox.SelectedIndex = Convert.ToInt32(AppSettings.Instance.UpdateChannel.Value);
        UpdateCheckBehaviorComboBox.SelectedIndex = Convert.ToInt32(AppSettings.Instance.UpdateCheckBehavior.Value);
        _latestUpdate = null;
        DownloadButton.IsVisible = false;
        UpdateProgressBar.IsVisible = false;
    }

    private async void CheckUpdateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击了检查更新按钮");
        CheckUpdateButton.IsEnabled = false;
        DownloadButton.IsVisible = false;
        UpdateProgressBar.IsVisible = false;
        _latestUpdate = null;
        UpdateStatusItem.Content = "正在检查更新";
        UpdateStatusItem.Description = "正在连接更新服务器，请稍候...";

        try
        {
            var result = await UpdateService.CheckUpdateAsync();
            if (result is null)
            {
                UpdateStatusItem.Content = "检查更新失败";
                UpdateStatusItem.Description = "未能获取到更新信息，请稍后重试。";
            }
            else if (result.HasUpdate)
            {
                _latestUpdate = result;
                UpdateStatusItem.Content = $"发现新版本 {result.LatestVersion}";
                UpdateStatusItem.Description = string.IsNullOrWhiteSpace(result.UpdateLog)
                    ? "有新版本可用。"
                    : result.UpdateLog;
                DownloadButton.IsVisible = !string.IsNullOrWhiteSpace(result.DownloadUrl);
            }
            else
            {
                UpdateStatusItem.Content = "已是最新版本";
                UpdateStatusItem.Description = $"当前版本 {UpdateService.GetCurrentVersion()} 已是最新。";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "检查更新失败");
            UpdateStatusItem.Content = "检查更新失败";
            UpdateStatusItem.Description = ex.Message;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void DownloadButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_latestUpdate is null || string.IsNullOrWhiteSpace(_latestUpdate.DownloadUrl))
        {
            _logger.Warning("没有可下载的更新");
            return;
        }

        _logger.Information("用户点击了下载更新按钮，版本: {Version}", _latestUpdate.LatestVersion);
        CheckUpdateButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        UpdateProgressBar.IsVisible = true;
        UpdateProgressBar.Value = 0;
        UpdateStatusItem.Content = "正在下载更新";
        UpdateStatusItem.Description = "下载进度 0%";

        var progress = new Progress<double>(value =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateProgressBar.Value = value;
                UpdateStatusItem.Description = $"下载进度 {value:F0}%";
            });
        });

        try
        {
            var filePath = await UpdateService.DownloadUpdateAsync(_latestUpdate, progress);
            if (filePath is null)
            {
                UpdateStatusItem.Content = "下载失败";
                UpdateStatusItem.Description = "未获取到有效的下载地址。";
            }
            else
            {
                UpdateProgressBar.Value = 100;
                UpdateStatusItem.Content = "更新已下载";
                UpdateStatusItem.Description = "更新已下载完成，将在下次启动时应用。";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "下载更新失败");
            UpdateStatusItem.Content = "下载失败";
            UpdateStatusItem.Description = ex.Message;
        }
        finally
        {
            UpdateProgressBar.IsVisible = false;
            DownloadButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void UpdateChannelComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (UpdateChannelComboBox.SelectedIndex >= 0)
        {
            var channel = (UpdateChannel)UpdateChannelComboBox.SelectedIndex;
            _logger.Information("更新渠道变更: {Channel}", channel);
            AppSettings.Instance.UpdateChannel.Value = channel;
        }
    }

    private void UpdateCheckBehaviorComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (UpdateCheckBehaviorComboBox.SelectedIndex >= 0)
        {
            var behavior = (UpdateCheckBehavior)UpdateCheckBehaviorComboBox.SelectedIndex;
            _logger.Information("更新行为变更: {Behavior}", behavior);
            AppSettings.Instance.UpdateCheckBehavior.Value = behavior;
        }
    }
}
