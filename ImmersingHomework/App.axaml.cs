using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Enums;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Services;
using ImmersingHomework.Services.Platforms;
using ImmersingHomework.Views;
using Serilog;

namespace ImmersingHomework;

public partial class App : Application
{
    private readonly ILogger _logger = Log.ForContext<App>();
    private WelcomeWindow? _welcomeWindow;
    private MainWindow? _mainWindow;
    private FloatingButtonWindow? _floatingButtonWindow;
    private SettingsWindow? _settingsWindow;
    private PlatformServiceBase? _platformService;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private TrayIcon? _trayIcon;
    private bool _isShowingExceptionWindow;
    
    public static readonly HttpClient HttpClient = new();
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // 设置全局默认字体
        var harmonyFont = new FontFamily("avares://ImmersingHomework/Assets/Fonts/HarmonyOS_SansSC_Regular.ttf#HarmonyOS Sans SC");
        Resources["ContentControlThemeFontFamily"] = harmonyFont;
        Resources["FontFamily"] = harmonyFont;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _logger.Information("应用框架初始化完成");

        RegisterGlobalExceptionHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;

            if (!Program.IsSingleInstance)
            {
                _logger.Warning("检测到已有实例运行，显示多实例提示窗口");
                desktop.MainWindow = new InstanceAlreadyRunningWindow();
                desktop.MainWindow.Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }
        }

        AppSettings.Instance.Initialize();
        _logger.Information("应用设置已初始化");

        if (AppSettings.Instance.EnableClassIslandIPCService.Value)
        {
            _logger.Information("ClassIsland 联动已启用，初始化 ClassIsland 服务");
            ClassIslandService.Instance.Initialize();
        }
        
        if (_desktopLifetime != null)
        {
            _platformService = CreatePlatformService();
            
            // 应用当前的开机自启动设置
            ApplyLaunchAtStartupSetting();
            // 订阅设置变更事件
            SubscribeToLaunchAtStartupChanges();

            if (!AppSettings.Instance.FirstLaunch)
            {
                _mainWindow = new MainWindow();
                _floatingButtonWindow = new FloatingButtonWindow();
            
                if (_platformService != null)
                {
                    _platformService.SetTopmost(_floatingButtonWindow);
                    _platformService.DisableFocus(_floatingButtonWindow);
                    _platformService.HideFromTaskbar(_floatingButtonWindow);
                    _platformService.HideFromAltTab(_floatingButtonWindow);
                }
                
                _desktopLifetime.MainWindow = _floatingButtonWindow;
            
                _mainWindow.WindowMinimized += MainWindow_WindowMinimized;
                _mainWindow.WindowActivated += MainWindow_WindowActivated;
                _mainWindow.WindowDeactivated += MainWindow_WindowDeactivated;
                _mainWindow.Closing += MainWindow_Closing;
            
                _floatingButtonWindow.FloatingButtonClicked += FloatingButtonWindow_FloatingButtonClicked;
                _floatingButtonWindow.Closing += FloatingButtonWindow_Closing;
            
                _floatingButtonWindow.ShowWithAnimation();
            
                SetupTrayIcon();
            
                if (AppSettings.Instance.EnableClassIslandIPCService.Value &&
                    ClassIslandService.Instance.IsCurrentTimeBeforeFirstClass() &&
                    AppSettings.Instance.ShowHomeworkBeforeFirstClassNextDay.Value)
                {
                    _logger.Information("当前时间在第一节课前，显示主界面");
                    ShowMainWindow();
                }
            }
            else
            {
                _welcomeWindow = new WelcomeWindow();
                _desktopLifetime.MainWindow = _welcomeWindow;
                _welcomeWindow.Show();
            }

            if (!AppSettings.Instance.FirstLaunch)
            {
                _logger.Information("启动时自动检查更新");
                _ = StartupUpdateCheckAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString());
            _logger.Fatal(ex, "未处理的异常 (AppDomain)");
            ShowExceptionWindow(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            _logger.Error(e.Exception, "未观察的任务异常");
            ShowExceptionWindow(e.Exception);
        };
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _logger.Fatal(e.Exception, "未处理的异常 (UI 线程)");
        ShowExceptionWindow(e.Exception);
    }

    private void ShowExceptionWindow(Exception ex)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowExceptionWindow(ex));
            return;
        }

        if (_isShowingExceptionWindow)
            return;
        _isShowingExceptionWindow = true;

        try
        {
            new ExceptionWindow(ex.ToString()).Show();
        }
        catch (Exception windowEx)
        {
            _logger.Error(windowEx, "显示异常窗口失败");
        }
    }

    private void ApplyLaunchAtStartupSetting()
    {
        if (_platformService != null)
        {
            _logger.Information("应用开机自启动设置: {Value}", AppSettings.Instance.LaunchAtStartup.Value);
            _platformService.SetLaunchAtStartup(AppSettings.Instance.LaunchAtStartup.Value);
        }
    }

    private void SubscribeToLaunchAtStartupChanges()
    {
        AppSettings.Instance.LaunchAtStartup.ValueChanged += (newValue) =>
        {
            _logger.Information("开机自启动设置变更，新值: {Value}", newValue);
            if (_platformService != null)
            {
                _platformService.SetLaunchAtStartup(newValue);
            }
        };
    }

    private void SetupTrayIcon()
    {
        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons.Count > 0)
        {
            _trayIcon = trayIcons[0];
            _trayIcon.ToolTipText = "方圆作业板";
            
            if (_trayIcon.Menu != null && _trayIcon.Menu.Items.Count > 0)
            {
                foreach (var item in _trayIcon.Menu.Items)
                {
                    if (item is NativeMenuItem menuItem)
                    {
                        menuItem.Click += TrayMenuItem_Click;
                    }
                }
            }
        }
    }

    private void TrayMenuItem_Click(object? sender, EventArgs e)
    {
        if (sender is not NativeMenuItem menuItem)
            return;

        var header = menuItem.Header?.ToString();

        // 托盘菜单点击可能在非 UI 线程（如 Linux 的 DBus）回调，
        // 统一投递到 UI 线程，确保异常能被 Dispatcher.UnhandledException 捕获。
        Dispatcher.UIThread.Post(() => HandleTrayMenuItemClick(header));
    }

    private void HandleTrayMenuItemClick(string? header)
    {
        switch (header)
        {
            case "显示主窗口":
                ShowMainWindow();
                break;
            case "显示/隐藏浮窗":
                ToggleFloatingButton();
                break;
            case "打开设置窗口":
                OpenSettingsWindow();
                break;
            case "重启":
                RestartApplication();
                break;
            case "退出":
                ExitApplication();
                break;
        }
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
            _settingsWindow.Show();
        }
    }

    public void OpenHomeworkAssignmentRemindWindow()
    {
        var remindWindow = new HomeworkAssignmentRemindWindow();
        remindWindow.Activate();
        remindWindow.Show();
    }

    private void ToggleFloatingButton()
    {
        if (_floatingButtonWindow != null)
        {
            if (_floatingButtonWindow.IsVisible)
            {
                HideFloatingButton();
            }
            else
            {
                ShowFloatingButton();
            }
        }
    }

    public void RestartApplication()
    {
        if (_desktopLifetime != null)
        {
            var processPath = Environment.ProcessPath;
            Program.ReleaseLock();
            _desktopLifetime.Shutdown();
            if (!string.IsNullOrEmpty(processPath))
            {
                Process.Start(processPath);
            }
        }
    }

    public void ExitApplication()
    {
        _desktopLifetime?.Shutdown();
    }

    private PlatformServiceBase CreatePlatformService()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatformService();
        if (OperatingSystem.IsMacOS())
            return new MacOSPlatformService();
        if (OperatingSystem.IsLinux())
        {
            var xdgSession = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (!string.IsNullOrEmpty(xdgSession))
            {
                if (xdgSession.Equals("x11", StringComparison.OrdinalIgnoreCase))
                    return new X11PlatformService();
                if (xdgSession.Equals("wayland", StringComparison.OrdinalIgnoreCase))
                    return new WaylandPlatformService();
            }
            
            var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (!string.IsNullOrEmpty(waylandDisplay))
                return new WaylandPlatformService();
            
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            if (!string.IsNullOrEmpty(display))
                return new X11PlatformService();
            
            return new X11PlatformService(); // 默认使用 X11
        }
        throw new PlatformNotSupportedException();
    }

    private async void MainWindow_WindowMinimized(object? sender, EventArgs e)
    {
        ShowFloatingButton();
        // 等待一小段时间让浮窗显示完成，然后隐藏主窗口
        await Task.Delay(200);
        _mainWindow?.Hide();
    }

    private void MainWindow_WindowActivated(object? sender, EventArgs e)
    {
        HideFloatingButton();
    }

    private void MainWindow_WindowDeactivated(object? sender, EventArgs e)
    {
        if (_mainWindow != null && _mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
        {
            ShowFloatingButton();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
    }

    private void FloatingButtonWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _mainWindow?.Close();
    }

    private void FloatingButtonWindow_FloatingButtonClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    public void HideMainWindow()
    {
        if (_mainWindow is null) return;
        ShowFloatingButton();
        _mainWindow.Hide();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.WindowState = Avalonia.Controls.WindowState.FullScreen;
        _mainWindow.Activate();
        _mainWindow.Show();
        _mainWindow.HomeworkPanel.Refresh();
    }

    private void ShowFloatingButton()
    {
        _floatingButtonWindow?.ShowWithAnimation();
    }

    private void HideFloatingButton()
    {
        _floatingButtonWindow?.HideWithAnimation();
    }

    private async Task StartupUpdateCheckAsync()
    {
        var behavior = AppSettings.Instance.UpdateCheckBehavior.Value;
        if (behavior == UpdateCheckBehavior.Nothing)
        {
            _logger.Information("更新行为为“不执行任何操作”，跳过启动时检查更新");
            return;
        }

        CheckUpdateResponse? update;
        try
        {
            update = await UpdateService.CheckUpdateAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "启动时检查更新失败");
            return;
        }

        if (update is null || !update.HasUpdate)
        {
            _logger.Information("启动时检查更新完成，当前已是最新版本");
            return;
        }

        _logger.Information("启动时检查更新发现新版本: {Version}，更新行为: {Behavior}，是否强制更新: {IsForceUpdate}",
            update.LatestVersion, behavior, update.IsForceUpdate);

        if (update.IsForceUpdate)
        {
            _logger.Information("检测到强制更新，忽略更新行为直接下载并安装");
            await DownloadUpdateAsync(update, installImmediately: true);
            return;
        }

        switch (behavior)
        {
            case UpdateCheckBehavior.NoticeImmediately:
                SendUpdateNotification(update);
                break;
            case UpdateCheckBehavior.DownloadImmediately:
                await DownloadUpdateAsync(update, installImmediately: false);
                break;
            case UpdateCheckBehavior.InstallImmediately:
                await DownloadUpdateAsync(update, installImmediately: true);
                break;
        }
    }

    private void SendUpdateNotification(CheckUpdateResponse update)
    {
        var title = "方圆作业板 更新";
        var body = $"发现新版本 {update.LatestVersion}。";
        if (!string.IsNullOrWhiteSpace(update.UpdateLog))
            body += $"\n{update.UpdateLog}";

        _logger.Information("发送系统更新通知: {Title}", title);
        _platformService?.SendNotification(title, body);
    }

    private async Task DownloadUpdateAsync(CheckUpdateResponse update, bool installImmediately)
    {
        var window = _desktopLifetime?.MainWindow;
        if (window is null)
        {
            try
            {
                await UpdateService.DownloadUpdateAsync(update);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "启动时下载更新失败");
            }
            return;
        }

        using var cts = new CancellationTokenSource();

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Width = 300
        };
        var statusText = new TextBlock { Text = "下载进度 0%" };
        var contentPanel = new StackPanel
        {
            Spacing = 12,
            Children = { statusText, progressBar }
        };

        var dialog = new FAContentDialog
        {
            Title = $"正在下载更新 {update.LatestVersion}",
            Content = contentPanel,
            CloseButtonText = "取消"
        };
        dialog.CloseButtonClick += (_, _) =>
        {
            cts.Cancel();
            dialog.Hide();
        };

        var progress = new Progress<double>(value =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                progressBar.Value = value;
                statusText.Text = $"下载进度 {value:F0}%";
            });
        });

        var showTask = dialog.ShowAsync(window);

        string? filePath = null;
        var cancelled = false;
        var downloadFailed = false;
        try
        {
            filePath = await UpdateService.DownloadUpdateAsync(update, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            _logger.Information("用户取消了更新下载");
        }
        catch (Exception ex)
        {
            downloadFailed = true;
            _logger.Error(ex, "启动时下载更新失败");
        }

        dialog.Hide();
        await showTask;

        if (cancelled)
            return;

        if (downloadFailed || filePath is null)
        {
            await ShowUpdateFailedDialogAsync(window);
            return;
        }

        if (installImmediately)
            await ShowRestartDialogAsync(window);
        else
            await ShowDownloadCompletedDialogAsync(window);
    }

    private async Task ShowDownloadCompletedDialogAsync(Window window)
    {
        var dialog = new FAContentDialog
        {
            Title = "更新下载完成",
            Content = "更新已下载完成，将在下次启动时应用。",
            CloseButtonText = "知道了"
        };
        dialog.CloseButtonClick += (_, _) => dialog.Hide();
        await dialog.ShowAsync(window);
    }

    private async Task ShowRestartDialogAsync(Window window)
    {
        var dialog = new FAContentDialog
        {
            Title = "更新已就绪",
            Content = "更新已下载完成，需要重启软件以应用更新。",
            PrimaryButtonText = "立即重启",
            CloseButtonText = "稍后"
        };
        dialog.PrimaryButtonClick += (_, _) =>
        {
            dialog.Hide();
            RestartApplication();
        };
        dialog.CloseButtonClick += (_, _) => dialog.Hide();
        await dialog.ShowAsync(window);
    }

    private async Task ShowUpdateFailedDialogAsync(Window window)
    {
        var dialog = new FAContentDialog
        {
            Title = "更新下载失败",
            Content = "更新下载失败，请稍后重试。",
            CloseButtonText = "知道了"
        };
        dialog.CloseButtonClick += (_, _) => dialog.Hide();
        await dialog.ShowAsync(window);
    }
}