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

            var current = Run("xdg-mime", $"query default x-scheme-handler/{Scheme}");
            return string.Equals(current, DesktopFileName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "检查 URL 协议注册状态失败");
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
                Logger.Error("Could not get executable path");
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

            Run("xdg-mime", $"default {DesktopFileName} x-scheme-handler/{Scheme}");
            Logger.Information("已注册 URL 协议: {Scheme}", Scheme);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "注册 URL 协议失败");
        }
    }

    public static void Unregister()
    {
        try
        {
            if (File.Exists(DesktopFilePath)) File.Delete(DesktopFilePath);
            Logger.Information("已注销 URL 协议: {Scheme}", Scheme);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "注销 URL 协议失败");
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string Run(string fileName, string arguments)
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
}
