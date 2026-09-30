using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Avalonia.Platform;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

/// <summary>
/// X11 / Wayland 共用的桌面快捷方式实现，基于 freedesktop.org 的 .desktop 规范。
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxShortcutService
{
    private const string ShortcutFileName = "方圆作业板.desktop";
    private static readonly ILogger Logger = Log.ForContext(typeof(LinuxShortcutService));

    private static string DesktopDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop");

    private static string ShortcutPath => Path.Combine(DesktopDirectory, ShortcutFileName);

    public static void Create()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                Logger.Error("创建桌面快捷方式失败: 无法获取可执行文件路径");
                return;
            }

            Directory.CreateDirectory(DesktopDirectory);

            var iconPath = ResolveIconPath();
            var content = $@"[Desktop Entry]
Type=Application
Version=1.0
Name=ImmersingHomework
Name[zh_CN]=方圆作业板
Comment=Immersing Homework Management
Exec={Quote(exePath)}
TryExec={Quote(exePath)}
Icon={iconPath}
Terminal=false
StartupNotify=true
Categories=Education;";

            File.WriteAllText(ShortcutPath, content);
            // GNOME / KDE 只会执行桌面目录中带可执行位的 .desktop 文件
            File.SetUnixFileMode(
                ShortcutPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            Logger.Information("桌面快捷方式创建成功: {Path}", ShortcutPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "创建桌面快捷方式失败");
        }
    }

    public static void Show()
    {
        try
        {
            if (!File.Exists(ShortcutPath))
            {
                Logger.Warning("桌面快捷方式不存在，先执行创建: {Path}", ShortcutPath);
                Create();
            }

            if (!TryRun("xdg-open", DesktopDirectory))
                TryRun("gio", $"open \"{ShortcutPath}\"");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "显示桌面快捷方式失败");
        }
    }

    /// <summary>
    /// 图标随程序打包为 Avalonia 资源，需先导出到本地文件系统供 .desktop 引用。
    /// </summary>
    private static string ResolveIconPath()
    {
        var iconDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "immersinghomework");
        var iconPath = Path.Combine(iconDirectory, "icon.png");

        try
        {
            Directory.CreateDirectory(iconDirectory);
            if (!File.Exists(iconPath))
            {
                var loader = new StandardAssetLoader(typeof(LinuxShortcutService).Assembly);
                using var source = loader.Open(new Uri("avares://ImmersingHomework/Assets/icon.png"));
                using var target = File.Create(iconPath);
                source.CopyTo(target);
            }

            return iconPath;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "导出图标失败，桌面快捷方式将使用默认图标");
            return "immersinghomework";
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static bool TryRun(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Logger.Warning("命令执行失败: {FileName} {Arguments}，退出码: {Code}", fileName, arguments, process.ExitCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "命令执行异常: {FileName} {Arguments}", fileName, arguments);
            return false;
        }
    }
}