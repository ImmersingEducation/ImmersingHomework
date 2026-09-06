using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Avalonia.Controls;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

[SupportedOSPlatform("windows")]
public class WindowsPlatformService : PlatformServiceBase
{
    private readonly ILogger _logger = Log.ForContext<WindowsPlatformService>();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
    
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    public override void SetTopmost(Window window, bool enable = true)
    {
        window.Opened += (sender, e) =>
        {
            window.Topmost = enable;
            if (enable && window.TryGetPlatformHandle()?.Handle is IntPtr hwnd)
            {
                SetForegroundWindow(hwnd);
            }
        };
    }

    public override void DisableFocus(Window window)
    {
        window.Focusable = false;
        window.ShowActivated = false;
        
        window.Opened += (sender, e) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is IntPtr hwnd)
            {
                var currentStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, currentStyle | WS_EX_NOACTIVATE);
            }
        };
    }

    public override void HideFromTaskbar(Window window)
    {
        window.ShowInTaskbar = false;
    }

    public override void HideFromAltTab(Window window)
    {
        window.ShowInTaskbar = false;
        
        window.Opened += (sender, e) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is IntPtr hwnd)
            {
                var currentStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, currentStyle | WS_EX_TOOLWINDOW);
            }
        };
    }

    public override void SetLaunchAtStartup(bool enabled)
    {
        try
        {
            var appName = "ImmersingHomework";
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.Error("Could not get executable path");
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null)
            {
                _logger.Error("Could not open registry key");
                return;
            }

            if (enabled)
            {
                key.SetValue(appName, exePath);
                _logger.Information("Enabled launch at startup");
            }
            else
            {
                if (key.GetValue(appName) != null)
                {
                    key.DeleteValue(appName);
                }
                _logger.Information("Disabled launch at startup");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set launch at startup");
        }
    }

    public override void SendNotification(string title, string message)
    {
        try
        {
            var hwnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hwnd == IntPtr.Zero)
            {
                _logger.Warning("无法获取窗口句柄，跳过系统通知");
                return;
            }

            var data = new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = hwnd,
                uID = 0x0001,
                uFlags = NotifyIconFlags.Info,
                szInfo = message,
                szInfoTitle = title,
                uTimeout = 10000
            };

            if (!Shell_NotifyIcon(NotifyIconMessage.Add, ref data))
            {
                _logger.Warning("添加通知图标失败，系统通知可能无法显示");
                return;
            }

            Shell_NotifyIcon(NotifyIconMessage.Delete, ref data);
            _logger.Information("已发送系统通知: {Title}", title);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "发送系统通知失败: {Title}", title);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(NotifyIconMessage dwMessage, ref NotifyIconData lpData);

    private enum NotifyIconMessage : uint
    {
        Add = 0x00000000,
        Delete = 0x00000002
    }

    [Flags]
    private enum NotifyIconFlags : uint
    {
        Message = 0x00000001,
        Icon = 0x00000002,
        Tip = 0x00000004,
        Info = 0x00000010
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public NotifyIconFlags uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}