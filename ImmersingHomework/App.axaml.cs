using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Models;
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
        AppSettings.Instance.Initialize();
        _logger.Information("应用设置已初始化");
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;
            _platformService = CreatePlatformService();

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
                
                desktop.MainWindow = _floatingButtonWindow;
            
                _mainWindow.WindowMinimized += MainWindow_WindowMinimized;
                _mainWindow.WindowActivated += MainWindow_WindowActivated;
                _mainWindow.WindowDeactivated += MainWindow_WindowDeactivated;
                _mainWindow.Closing += MainWindow_Closing;
            
                _floatingButtonWindow.FloatingButtonClicked += FloatingButtonWindow_FloatingButtonClicked;
                _floatingButtonWindow.Closing += FloatingButtonWindow_Closing;
            
                _floatingButtonWindow.ShowWithAnimation();

                SetupTrayIcon();
            }
            else
            {
                _welcomeWindow = new WelcomeWindow();
                desktop.MainWindow = _welcomeWindow;
                _welcomeWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons.Count > 0)
        {
            _trayIcon = trayIcons[0];
            _trayIcon.ToolTipText = "ImmersingHomework";
            
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
        if (sender is NativeMenuItem menuItem)
        {
            switch (menuItem.Header)
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
            var processName = Process.GetCurrentProcess().ProcessName;
            var processPath = Environment.ProcessPath;
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

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.WindowState = Avalonia.Controls.WindowState.FullScreen;
            _mainWindow.Activate();
            _mainWindow.Show();
        }
    }

    private void ShowFloatingButton()
    {
        _floatingButtonWindow?.ShowWithAnimation();
    }

    private void HideFloatingButton()
    {
        _floatingButtonWindow?.HideWithAnimation();
    }
}