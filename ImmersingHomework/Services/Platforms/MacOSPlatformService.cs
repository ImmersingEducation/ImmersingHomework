using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Controls;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

[SupportedOSPlatform("macos")]
public class MacOSPlatformService : PlatformServiceBase
{
    private readonly ILogger _logger = Log.ForContext<MacOSPlatformService>();
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern void NSWindowSetLevel(IntPtr window, int level);
    
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern void NSWindowSetCollectionBehavior(IntPtr window, int behavior);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, int encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern long CFStringGetLength(IntPtr theString);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, int bufferSize, int encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFBundleGetMainBundle();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFBundleGetIdentifier(IntPtr bundle);

    [DllImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")]
    private static extern int LSSetDefaultHandlerForURLScheme(IntPtr scheme, IntPtr bundleId);

    [DllImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")]
    private static extern IntPtr LSCopyDefaultHandlerForURLScheme(IntPtr scheme);

    private const int kCGWindowLevelFloating = 3;
    private const int NSWindowCollectionBehaviorIgnoreCycle = 1 << 5;
    private const string UrlScheme = "immersinghomework";
    private const string ShortcutName = "方圆作业板";
    private const int kCFStringEncodingUTF8 = 0x08000100;

    public override void SetTopmost(Window window, bool enable = true)
    {
        window.Opened += (sender, e) =>
        {
            window.Topmost = enable;
            window.ShowInTaskbar = false;
            if (enable && window.TryGetPlatformHandle()?.Handle is IntPtr nsWindow)
            {
                NSWindowSetLevel(nsWindow, kCGWindowLevelFloating);
            }
        };
    }

    public override void DisableFocus(Window window)
    {
        window.Focusable = false;
        window.ShowActivated = false;
        
        window.Opened += (sender, e) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is IntPtr nsWindow)
            {
                NSWindowSetCollectionBehavior(nsWindow, NSWindowCollectionBehaviorIgnoreCycle);
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
            if (window.TryGetPlatformHandle()?.Handle is IntPtr nsWindow)
            {
                NSWindowSetCollectionBehavior(nsWindow, NSWindowCollectionBehaviorIgnoreCycle);
            }
        };
    }

    public override void SetLaunchAtStartup(bool enabled)
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.Error("Could not get executable path");
                return;
            }
            
            var script = enabled
                ? $"tell application \"System Events\" to make login item at end with properties {{name:\"ImmersingHomework\", path:\"{exePath}\", hidden:false}}"
                : $"tell application \"System Events\" to delete login item \"ImmersingHomework\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                _logger.Information(enabled ? "Enabled launch at startup" : "Disabled launch at startup");
            }
            else
            {
                var error = process.StandardError.ReadToEnd();
                _logger.Error($"Failed to set launch at startup: {error}");
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
            var script = $"display notification \"{message.Replace("\"", "\\\"")}\" with title \"{title.Replace("\"", "\\\"")}\"";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e \"{script}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                _logger.Error("发送系统通知失败: {Error}", error);
            }
            else
            {
                _logger.Information("已发送系统通知: {Title}", title);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "发送系统通知失败: {Title}", title);
        }
    }

    public override bool IsUrlSchemaRegistered
    {
        get
        {
            var scheme = CreateCfString(UrlScheme);
            try
            {
                var handler = LSCopyDefaultHandlerForURLScheme(scheme);
                if (handler == IntPtr.Zero) return false;
                try
                {
                    var handlerId = CfStringToString(handler);
                    var bundleId = CurrentBundleIdentifier;
                    return !string.IsNullOrEmpty(bundleId) &&
                           string.Equals(handlerId, bundleId, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    CFRelease(handler);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "检查 URL 协议注册状态失败: {Scheme}", UrlScheme);
                return false;
            }
            finally
            {
                CFRelease(scheme);
            }
        }
        set
        {
            var scheme = CreateCfString(UrlScheme);
            var bundleId = CreateCfString(CurrentBundleIdentifier ?? string.Empty);
            try
            {
                if (string.IsNullOrEmpty(CurrentBundleIdentifier))
                {
                    _logger.Error("URL 协议注册失败: 无法获取应用 Bundle Identifier，{Scheme}", UrlScheme);
                    return;
                }

                if (value)
                {
                    var result = LSSetDefaultHandlerForURLScheme(scheme, bundleId);
                    if (result == 0) _logger.Information("URL 协议注册成功: {Scheme}", UrlScheme);
                    else _logger.Error("URL 协议注册失败: {Scheme}，错误码: {Code}", UrlScheme, result);
                }
                else
                {
                    _logger.Warning("URL 协议注销失败: macOS 暂不支持运行时注销 {Scheme}", UrlScheme);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "URL 协议注册失败: {Scheme}", UrlScheme);
            }
            finally
            {
                CFRelease(scheme);
                CFRelease(bundleId);
            }
        }
    }

    private static IntPtr CreateCfString(string value) =>
        CFStringCreateWithCString(IntPtr.Zero, value, kCFStringEncodingUTF8);

    private static string? CfStringToString(IntPtr cf)
    {
        if (cf == IntPtr.Zero) return null;

        var length = CFStringGetLength(cf);
        if (length == 0) return string.Empty;

        var buffer = new byte[length * 4 + 1];
        return CFStringGetCString(cf, buffer, buffer.Length, kCFStringEncodingUTF8)
            ? Encoding.UTF8.GetString(buffer).TrimEnd('\0')
            : null;
    }

    private static string? CurrentBundleIdentifier
    {
        get
        {
            var bundle = CFBundleGetMainBundle();
            if (bundle == IntPtr.Zero) return null;

            var identifier = CFBundleGetIdentifier(bundle);
            return CfStringToString(identifier);
        }
    }

    public override void CreateDesktopShortcut()
    {
        CreateFinderAlias(GetDesktopDirectory(), "桌面");
    }

    public override void CreateStartMenuShortcut()
    {
        CreateFinderAlias(GetApplicationsDirectory(), "应用程序");
    }

    /// <summary>
    /// 在目标目录创建指向当前 .app 包的 Finder 别名。
    /// </summary>
    private void CreateFinderAlias(string targetDirectory, string location)
    {
        try
        {
            var appBundle = FindAppBundle();
            if (string.IsNullOrEmpty(appBundle))
            {
                _logger.Warning("当前未运行在 .app 包内，跳过{Location}快捷方式创建", location);
                return;
            }

            Directory.CreateDirectory(targetDirectory);

            var shortcutPath = Path.Combine(targetDirectory, ShortcutName);
            if (Directory.Exists(shortcutPath)) Directory.Delete(shortcutPath, true);
            else if (File.Exists(shortcutPath)) File.Delete(shortcutPath);

            // Finder 的 alias 文件支持双击启动应用且自带图标
            var script = $"tell application \"Finder\"\n" +
                          $"    set targetFolder to (POSIX file \"{EscapeAppleScript(targetDirectory)}\" as alias)\n" +
                          $"    set targetApp to (POSIX file \"{EscapeAppleScript(appBundle)}\" as alias)\n" +
                          $"    set newAlias to make new alias file at targetFolder to targetApp\n" +
                          $"    set name of newAlias to \"{EscapeAppleScript(ShortcutName)}\"\n" +
                          "end tell";

            if (!TryRunOsaScript(script))
            {
                _logger.Error("创建{Location}快捷方式失败: Finder 别名创建未成功", location);
                return;
            }

            _logger.Information("{Location}快捷方式创建成功: {Path}", location, shortcutPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "创建{Location}快捷方式失败", location);
        }
    }

    public override void ShowDesktopShortcut()
    {
        try
        {
            var desktopDirectory = GetDesktopDirectory();
            var shortcutPath = Path.Combine(desktopDirectory, ShortcutName);
            var target = Directory.Exists(shortcutPath) || File.Exists(shortcutPath)
                ? shortcutPath
                : desktopDirectory;

            // -R 会在访达中选中该文件
            var startInfo = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
            startInfo.ArgumentList.Add("-R");
            startInfo.ArgumentList.Add(target);

            using var process = Process.Start(startInfo);
            process?.WaitForExit();

            if (process is { ExitCode: not 0 })
                _logger.Error("显示桌面快捷方式失败，退出码: {Code}", process.ExitCode);
            else
                _logger.Information("已显示桌面快捷方式: {Path}", target);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "显示桌面快捷方式失败");
        }
    }

    private static string GetDesktopDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop");

    /// <summary>
    /// 「开始菜单」的对应位置为应用程序目录，系统目录不可写时回退到用户目录。
    /// </summary>
    private static string GetApplicationsDirectory()
    {
        const string systemApplications = "/Applications";
        if (Directory.Exists(systemApplications) && HasWritePermission(systemApplications))
            return systemApplications;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Applications");
    }

    private static bool HasWritePermission(string directory)
    {
        try
        {
            var probePath = Path.Combine(directory, $".immersinghomework-{Guid.NewGuid():N}");
            using (File.Create(probePath)) { }
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 向上查找当前进程所在的 .app 包路径，非包内运行时返回 null。
    /// </summary>
    private static string? FindAppBundle()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return null;

        var directory = Path.GetDirectoryName(exePath);
        while (!string.IsNullOrEmpty(directory))
        {
            if (directory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return directory;
            var parent = Path.GetDirectoryName(directory);
            if (string.Equals(parent, directory, StringComparison.Ordinal)) break;
            directory = parent;
        }

        return null;
    }

    private static string EscapeAppleScript(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private bool TryRunOsaScript(string script)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo);
        if (process == null) return false;
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd().Trim();
            _logger.Error("osascript 执行失败，退出码: {Code}，错误: {Error}", process.ExitCode, error);
            return false;
        }

        return true;
    }
}
