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
}
