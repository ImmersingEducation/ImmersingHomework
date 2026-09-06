using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Avalonia.Controls;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

[SupportedOSPlatform("linux")]
public class X11PlatformService : PlatformServiceBase
{
    private readonly ILogger _logger = Log.ForContext<X11PlatformService>();
    public override void SetTopmost(Window window, bool enable = true)
    {
        window.Opened += (sender, e) =>
        {
            window.Topmost = enable;
        };
    }

    public override void DisableFocus(Window window)
    {
        window.Focusable = false;
        window.ShowActivated = false;
    }

    public override void HideFromTaskbar(Window window)
    {
        window.ShowInTaskbar = false;
    }

    public override void HideFromAltTab(Window window)
    {
        window.ShowInTaskbar = false;
    }

    public override void SetLaunchAtStartup(bool enabled)
    {
        SetLinuxLaunchAtStartup(enabled);
    }

    public override void SendNotification(string title, string message)
    {
        LinuxNotificationService.Send(title, message);
    }

    private void SetLinuxLaunchAtStartup(bool enabled)
    {
        try
        {
            var autostartDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart");
            
            Directory.CreateDirectory(autostartDir);
            
            var desktopFile = Path.Combine(autostartDir, "immersinghomework.desktop");
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;

            if (string.IsNullOrEmpty(exePath))
            {
                _logger.Error("Could not get executable path");
                return;
            }

            if (enabled)
            {
                var content = @$"[Desktop Entry]
Type=Application
Name=ImmersingHomework
Exec={exePath}
Comment=Immersing Homework Management
Terminal=false
Categories=Education;";
                File.WriteAllText(desktopFile, content);
                _logger.Information("Enabled launch at startup");
            }
            else
            {
                if (File.Exists(desktopFile))
                {
                    File.Delete(desktopFile);
                }
                _logger.Information("Disabled launch at startup");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set launch at startup");
        }
    }
}
