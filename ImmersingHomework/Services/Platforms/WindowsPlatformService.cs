using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Controls;
using ImmersingHomework.Abstractions;
using Microsoft.Win32;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

[SupportedOSPlatform("windows")]
public class WindowsPlatformService : PlatformServiceBase
{
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const string UrlScheme = "immersinghomework";
    private const string ShortcutName = "方圆作业板";
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private readonly ILogger _logger = Log.ForContext<WindowsPlatformService>();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    public override void SetTopmost(Window window, bool enable = true)
    {
        window.Opened += (sender, e) =>
        {
            window.Topmost = enable;
            if (enable && window.TryGetPlatformHandle()?.Handle is IntPtr hwnd) SetForegroundWindow(hwnd);
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
                if (key.GetValue(appName) != null) key.DeleteValue(appName);
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

    public override bool IsUrlSchemaRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{UrlScheme}");
                return key != null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "检查 URL 协议注册状态失败: {Scheme}", UrlScheme);
                return false;
            }
        }
        set
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    _logger.Error("URL 协议注册失败: 无法获取可执行文件路径");
                    return;
                }

                var schemeKeyPath = $@"Software\Classes\{UrlScheme}";
                if (value)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(schemeKeyPath, true);
                    if (key == null)
                    {
                        _logger.Error("URL 协议注册失败: 无法创建注册表项 {Scheme}", UrlScheme);
                        return;
                    }

                    key.SetValue(null, $"URL:{UrlScheme} Protocol");
                    key.SetValue("URL Protocol", string.Empty);

                    using var commandKey = key.CreateSubKey(@"shell\open\command");
                    if (commandKey == null)
                    {
                        _logger.Error("URL 协议注册失败: 无法创建命令注册表项 {Scheme}", UrlScheme);
                        return;
                    }

                    commandKey.SetValue(null, $"\"{exePath}\" \"%1\"");
                    _logger.Information("URL 协议注册成功: {Scheme}", UrlScheme);
                }
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(schemeKeyPath, false);
                    _logger.Information("URL 协议注销成功: {Scheme}", UrlScheme);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "URL 协议注册失败: {Scheme}", UrlScheme);
            }
        }
    }

    public override void CreateDesktopShortcut()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.Error("创建桌面快捷方式失败: 无法获取可执行文件路径");
                return;
            }

            var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktopDirectory))
            {
                _logger.Error("创建桌面快捷方式失败: 无法获取桌面目录");
                return;
            }

            Directory.CreateDirectory(desktopDirectory);
            var shortcutPath = Path.Combine(desktopDirectory, $"{ShortcutName}.lnk");

            var shellLinkType = Type.GetTypeFromCLSID(ShellLinkClassId);
            if (shellLinkType == null)
            {
                _logger.Error("创建桌面快捷方式失败: 无法创建 ShellLink COM 对象");
                return;
            }

            if (Activator.CreateInstance(shellLinkType) is not IShellLinkW shellLink)
            {
                _logger.Error("创建桌面快捷方式失败: ShellLink COM 对象创建失败");
                return;
            }

            shellLink.SetPath(exePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);
            shellLink.SetDescription("Immersing Homework Management");
            // 图标已嵌入可执行文件，直接引用自身图标
            shellLink.SetIconLocation(exePath, 0);
            ((IPersistFile)shellLink).Save(shortcutPath, true);

            _logger.Information("桌面快捷方式创建成功: {Path}", shortcutPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "创建桌面快捷方式失败");
        }
    }

    public override void ShowDesktopShortcut()
    {
        try
        {
            var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktopDirectory))
            {
                _logger.Error("显示桌面快捷方式失败: 无法获取桌面目录");
                return;
            }

            var shortcutPath = Path.Combine(desktopDirectory, $"{ShortcutName}.lnk");
            if (!File.Exists(shortcutPath))
            {
                _logger.Warning("桌面快捷方式不存在，直接打开桌面目录: {Path}", shortcutPath);
                OpenInExplorer(desktopDirectory);
                return;
            }

            OpenInExplorer($"/select,\"{shortcutPath}\"");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "显示桌面快捷方式失败");
        }
    }

    private void OpenInExplorer(string target)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}