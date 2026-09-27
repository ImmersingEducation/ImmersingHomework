using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

internal static class LinuxUrlSchemeService
{
    private const string Scheme = "immersinghomework";
    private const string DesktopFileName = "immersinghomework.desktop";
    private static readonly ILogger Logger = Log.ForContext(typeof(LinuxUrlSchemeService));

    private static string DesktopFileDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "applications");

    private static string DesktopFilePath => Path.Combine(DesktopFileDirectory, DesktopFileName);

    public static bool IsRegistered()
    {
        try
        {
            if (!File.Exists(DesktopFilePath)) return false;

            var current = Query("xdg-mime", $"query default x-scheme-handler/{Scheme}");
            return string.Equals(current, DesktopFileName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "检查 URL 协议注册状态失败: {Scheme}", Scheme);
            return false;
        }
    }

    public static void Register()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                Logger.Error("URL 协议注册失败: 无法获取可执行文件路径");
                return;
            }

            Directory.CreateDirectory(DesktopFileDirectory);

            var content = $@"[Desktop Entry]
Type=Application
Name=ImmersingHomework
Exec={Quote(exePath)} %u
Comment=Immersing Homework Management
Terminal=false
MimeType=x-scheme-handler/{Scheme};
Categories=Education;";
            File.WriteAllText(DesktopFilePath, content);

            var failed = false;
            failed |= !TryRun("xdg-mime", $"default {DesktopFileName} x-scheme-handler/{Scheme}");
            failed |= !TryRun("xdg-settings", $"set default-url-scheme-handler {Scheme} {DesktopFileName}");
            TryRun("update-desktop-database", DesktopFileDirectory);

            if (failed)
                Logger.Error("URL 协议注册失败: {Scheme}，可能缺少 xdg-utils", Scheme);
            else
                Logger.Information("URL 协议注册成功: {Scheme}", Scheme);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "URL 协议注册失败: {Scheme}", Scheme);
        }
    }

    public static void Unregister()
    {
        try
        {
            if (File.Exists(DesktopFilePath)) File.Delete(DesktopFilePath);
            Logger.Information("URL 协议注销成功: {Scheme}", Scheme);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "URL 协议注销失败: {Scheme}", Scheme);
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
                var error = process.StandardError.ReadToEnd().Trim();
                Logger.Warning("命令执行失败: {FileName} {Arguments}，退出码: {Code}，错误: {Error}",
                    fileName, arguments, process.ExitCode, error);
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

    private static string? Query(string fileName, string arguments)
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
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "查询命令异常: {FileName} {Arguments}", fileName, arguments);
            return null;
        }
    }
}
