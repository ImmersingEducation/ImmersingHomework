using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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

    private const int kCGWindowLevelFloating = 3;
    private const int NSWindowCollectionBehaviorIgnoreCycle = 1 << 5;

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
}
