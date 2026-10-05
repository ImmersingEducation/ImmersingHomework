using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Enums;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class UpdateSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly ILogger _logger = Log.ForContext<UpdateSettingsPage>();
    private readonly IUpdateService _updateService;

    /// <summary>最近一次检查到的更新信息，为空表示当前没有可下载的新版本。</summary>
    private CheckUpdateResponse? _latestUpdate;

    /// <summary>正在进行的下载任务的取消源，页面卸载时会用它中止下载。</summary>
    private CancellationTokenSource? _downloadCts;

    private PropertyChangedEventHandler? _propertyChanged;

    private UpdateStatus _status = UpdateStatus.Unknown;
    private UpdateWorkingStatus _workingStatus = UpdateWorkingStatus.Idle;
    private string _currentVersion = string.Empty;
    private string _platform = string.Empty;
    private string? _latestVersion;
    private string? _changeLog;
    private DateTimeOffset? _lastCheckTime;
    private double _downloadProgress;
    private bool _isForceUpdate;
    private Exception? _checkError;
    private Exception? _downloadError;
    private bool _checkErrorVisible;
    private bool _downloadErrorVisible;
    private int _selectedChannelIndex;
    private int _selectedCheckBehaviorIndex;

    /// <summary>更新状态 -> 顶部状态图标。</summary>
    public static readonly FuncValueConverter<UpdateStatus, FASymbol> UpdateStatusToIconConverter =
        new(x => x switch
        {
            UpdateStatus.UpToDate => FASymbol.Accept,
            UpdateStatus.UpdateAvailable => FASymbol.Download,
            UpdateStatus.UpdateDownloaded => FASymbol.Cloud,
            _ => FASymbol.Sync
        });

    /// <summary>更新状态 -> 顶部状态标题。</summary>
    public static readonly FuncValueConverter<UpdateStatus, string> UpdateStatusToMessageConverter =
        new(x => x switch
        {
            UpdateStatus.UpToDate => "您已更新到最新版本。",
            UpdateStatus.UpdateAvailable => "检测到新版本。",
            UpdateStatus.UpdateDownloaded => "更新已准备好。",
            _ => "尚未检查更新。"
        });

    /// <summary>当前任务 -> 进度区文案。</summary>
    public static readonly FuncValueConverter<UpdateWorkingStatus, string> UpdateWorkingStatusToMessageConverter =
        new(x => x switch
        {
            UpdateWorkingStatus.CheckingUpdates => "正在检查更新…",
            UpdateWorkingStatus.DownloadingUpdates => "正在下载更新…",
            _ => "就绪"
        });

    /// <summary>更新渠道下拉框选中项 -> 渠道说明。</summary>
    public static readonly FuncValueConverter<int, string> UpdateChannelToDescriptionConverter =
        new(x => (UpdateChannel)x == UpdateChannel.Beta
            ? "测试版包含尚在开发中的功能，可能存在不稳定的问题，请谨慎使用。"
            : "稳定版经过充分验证，推荐所有用户使用。");

    /// <summary>是否展示主操作“检查更新”按钮：当前没有待处理的新版本时。</summary>
    public static readonly FuncValueConverter<UpdateStatus, bool> ShowCheckUpdateConverter =
        new(x => x is UpdateStatus.Unknown or UpdateStatus.UpToDate);

    /// <summary>是否展示次要操作“重新检查更新”按钮：当前已有待处理的新版本时。</summary>
    public static readonly FuncValueConverter<UpdateStatus, bool> ShowRecheckUpdateConverter =
        new(x => x is UpdateStatus.UpdateAvailable or UpdateStatus.UpdateDownloaded);

    public UpdateSettingsPage(IUpdateService updateService)
    {
        _logger.Debug("UpdateSettingsPage 初始化");
        _updateService = updateService;

        _currentVersion = _updateService.GetCurrentVersion();
        _platform = _updateService.GetCurrentPlatform();
        _selectedChannelIndex = (int)AppSettings.Instance.UpdateChannel.Value;
        _selectedCheckBehaviorIndex = (int)AppSettings.Instance.UpdateCheckBehavior.Value;

        DataContext = this;
        InitializeComponent();
    }

    /// <summary>AvaloniaObject 已经实现了 <see cref="INotifyPropertyChanged"/>，这里显式实现接口成员，
    /// 避免与基类同名的 PropertyChanged 事件互相遮蔽。</summary>
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    public UpdateStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public UpdateWorkingStatus WorkingStatus
    {
        get => _workingStatus;
        set => SetField(ref _workingStatus, value);
    }

    public string CurrentVersion
    {
        get => _currentVersion;
        set => SetField(ref _currentVersion, value);
    }

    public string Platform
    {
        get => _platform;
        set => SetField(ref _platform, value);
    }

    public string? LatestVersion
    {
        get => _latestVersion;
        set => SetField(ref _latestVersion, value);
    }

    public string? ChangeLog
    {
        get => _changeLog;
        set => SetField(ref _changeLog, value);
    }

    public DateTimeOffset? LastCheckTime
    {
        get => _lastCheckTime;
        set => SetField(ref _lastCheckTime, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetField(ref _downloadProgress, value);
    }

    public bool IsForceUpdate
    {
        get => _isForceUpdate;
        set => SetField(ref _isForceUpdate, value);
    }

    /// <summary>检查更新的错误信息，InfoBar 直接显示其消息。</summary>
    public Exception? CheckError
    {
        get => _checkError;
        set => SetField(ref _checkError, value);
    }

    /// <summary>下载更新的错误信息，InfoBar 直接显示其消息。</summary>
    public Exception? DownloadError
    {
        get => _downloadError;
        set => SetField(ref _downloadError, value);
    }

    /// <summary>检查更新错误 InfoBar 的展开状态，关闭时顺带清空错误信息。</summary>
    public bool CheckErrorVisible
    {
        get => _checkErrorVisible;
        set
        {
            if (_checkErrorVisible == value)
                return;
            _checkErrorVisible = value;
            if (!value)
                CheckError = null;
            OnPropertyChanged();
        }
    }

    /// <summary>下载更新错误 InfoBar 的展开状态，关闭时顺带清空错误信息。</summary>
    public bool DownloadErrorVisible
    {
        get => _downloadErrorVisible;
        set
        {
            if (_downloadErrorVisible == value)
                return;
            _downloadErrorVisible = value;
            if (!value)
                DownloadError = null;
            OnPropertyChanged();
        }
    }

    /// <summary>更新渠道下拉框的选中项，变更后立即写回全局设置。</summary>
    public int SelectedChannelIndex
    {
        get => _selectedChannelIndex;
        set
        {
            if (_selectedChannelIndex == value)
                return;
            _selectedChannelIndex = value;
            var channel = (UpdateChannel)value;
            _logger.Information("更新渠道变更: {Channel}", channel);
            AppSettings.Instance.UpdateChannel.Value = channel;
            OnPropertyChanged();
        }
    }

    /// <summary>更新行为下拉框的选中项，变更后立即写回全局设置。</summary>
    public int SelectedCheckBehaviorIndex
    {
        get => _selectedCheckBehaviorIndex;
        set
        {
            if (_selectedCheckBehaviorIndex == value)
                return;
            _selectedCheckBehaviorIndex = value;
            var behavior = (UpdateCheckBehavior)value;
            _logger.Information("更新行为变更: {Behavior}", behavior);
            AppSettings.Instance.UpdateCheckBehavior.Value = behavior;
            OnPropertyChanged();
        }
    }

    /// <summary>顶部状态标题下方的补充说明，随更新状态切换。</summary>
    public string? StatusDetail => Status switch
    {
        UpdateStatus.UpToDate => $"当前版本 {CurrentVersion} 已是最新。",
        UpdateStatus.UpdateAvailable => $"发现新版本 {LatestVersion}，当前版本为 {CurrentVersion}。",
        UpdateStatus.UpdateDownloaded => $"新版本 {LatestVersion} 已下载完成，重启软件后生效。",
        _ => LastCheckTime is null
            ? $"当前版本 {CurrentVersion}，点击“检查更新”获取最新版本。"
            : $"上次检查更新时间：{LastCheckTime:yyyy-MM-dd HH:mm}"
    };

    /// <summary>是否存在可展示的更新日志。</summary>
    public bool HasChangeLog => !string.IsNullOrWhiteSpace(ChangeLog);

    public string LastCheckTimeText => LastCheckTime?.ToString("yyyy-MM-dd HH:mm") ?? "尚未检查";

    /// <summary>更新包所在的本地目录，随已发现的版本变化。</summary>
    public string UpdateDirectory => _updateService.GetUpdateDirectory(LatestVersion ?? CurrentVersion);

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _logger.Debug("UpdateSettingsPage 已加载，刷新页面状态");
        CurrentVersion = _updateService.GetCurrentVersion();
        Platform = _updateService.GetCurrentPlatform();
    }

    private void Control_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _logger.Debug("UpdateSettingsPage 已卸载");
        _downloadCts?.Cancel();
    }

    private void ButtonCheckUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = CheckUpdateAsync();
    }

    private async Task CheckUpdateAsync()
    {
        if (WorkingStatus != UpdateWorkingStatus.Idle)
        {
            _logger.Debug("已有更新任务进行中，忽略本次检查请求");
            return;
        }

        _logger.Information("用户请求检查更新");
        WorkingStatus = UpdateWorkingStatus.CheckingUpdates;
        CheckErrorVisible = false;
        CheckError = null;

        try
        {
            var result = await _updateService.CheckUpdateAsync();
            LastCheckTime = DateTimeOffset.Now;

            if (result is { HasUpdate: true })
            {
                _latestUpdate = result;
                LatestVersion = result.LatestVersion;
                ChangeLog = string.IsNullOrWhiteSpace(result.UpdateLog) ? null : result.UpdateLog;
                IsForceUpdate = result.IsForceUpdate;
                Status = UpdateStatus.UpdateAvailable;
                _logger.Information("检查更新完成，发现新版本: {Version}", result.LatestVersion);
            }
            else
            {
                ClearUpdateInfo();
                Status = UpdateStatus.UpToDate;
                _logger.Information("检查更新完成，当前已是最新版本");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "检查更新失败");
            ClearUpdateInfo();
            Status = UpdateStatus.Unknown;
            CheckError = ex;
            CheckErrorVisible = true;
        }
        finally
        {
            WorkingStatus = UpdateWorkingStatus.Idle;
        }
    }

    private void ButtonDownloadAndRestart_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = DownloadAndRestartAsync();
    }

    private void MenuItemDownloadOnly_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = DownloadUpdateAsync();
    }

    private async Task DownloadAndRestartAsync()
    {
        if (await DownloadUpdateAsync())
        {
            RestartApplication();
        }
    }

    /// <summary>下载最新版本的更新包，返回是否下载成功。</summary>
    private async Task<bool> DownloadUpdateAsync()
    {
        if (WorkingStatus != UpdateWorkingStatus.Idle)
        {
            _logger.Debug("已有更新任务进行中，忽略本次下载请求");
            return false;
        }

        if (_latestUpdate is null)
        {
            _logger.Warning("当前没有可下载的更新");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_latestUpdate.DownloadUrl))
        {
            _logger.Warning("更新包下载地址为空，无法下载");
            DownloadError = new InvalidOperationException($"更新服务器未提供 {Platform} 平台的更新包。");
            DownloadErrorVisible = true;
            return false;
        }

        _logger.Information("开始下载更新: {Version}", _latestUpdate.LatestVersion);
        WorkingStatus = UpdateWorkingStatus.DownloadingUpdates;
        DownloadProgress = 0;
        DownloadErrorVisible = false;
        DownloadError = null;

        var cts = new CancellationTokenSource();
        _downloadCts = cts;
        var progress = new Progress<double>(value => DownloadProgress = value);

        try
        {
            var filePath = await _updateService.DownloadUpdateAsync(_latestUpdate, progress, cts.Token);
            if (filePath is null)
            {
                DownloadError = new InvalidOperationException("未获取到有效的更新包。");
                DownloadErrorVisible = true;
                return false;
            }

            DownloadProgress = 100;
            Status = UpdateStatus.UpdateDownloaded;
            _logger.Information("更新下载完成: {FilePath}", filePath);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.Information("更新下载已取消");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "下载更新失败");
            DownloadError = ex;
            DownloadErrorVisible = true;
            return false;
        }
        finally
        {
            _downloadCts = null;
            cts.Dispose();
            WorkingStatus = UpdateWorkingStatus.Idle;
        }
    }

    private void ButtonCancelDownload_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户请求取消更新下载");
        _downloadCts?.Cancel();
    }

    private void ButtonRestart_OnClick(object? sender, RoutedEventArgs e)
    {
        RestartApplication();
    }

    private void MenuItemOpenUpdateDirectory_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenUpdateDirectory();
    }

    private void SettingsExpanderItemOpenUpdateDirectory_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenUpdateDirectory();
    }

    private void RestartApplication()
    {
        if (Application.Current is App app)
        {
            _logger.Information("用户请求重启软件以应用更新");
            app.RestartApplication();
            return;
        }

        _logger.Warning("当前应用实例不可用，无法重启");
    }

    private void OpenUpdateDirectory()
    {
        var directory = UpdateDirectory;
        try
        {
            _logger.Information("打开更新包目录: {Directory}", directory);
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "打开更新包目录失败: {Directory}", directory);
        }
    }

    /// <summary>清空上次检查到的更新信息，回到“尚未检查”的初始状态。</summary>
    private void ClearUpdateInfo()
    {
        _latestUpdate = null;
        LatestVersion = null;
        ChangeLog = null;
        IsForceUpdate = false;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(HasChangeLog));
        OnPropertyChanged(nameof(LastCheckTimeText));
        OnPropertyChanged(nameof(UpdateDirectory));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}