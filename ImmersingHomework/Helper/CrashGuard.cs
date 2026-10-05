using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace ImmersingHomework.Helper;

/// <summary>
/// 跨进程的崩溃计数，给“教学安全模式 → 自动重启”兜底。
/// </summary>
/// <remarks>
/// <para>
/// 短时间内反复崩溃说明重启解决不了问题，此时继续自动重启只会让用户看到应用无限闪退、
/// 而且拿不到任何错误信息；因此次数超阈值后改为弹窗，把现场留给用户。
/// </para>
/// <para>
/// 计数写在 <see cref="LauncherContext.RootDirectory"/> 下而不是当前工作目录：
/// <c>App.RestartApplication()</c> 会把子进程的工作目录设为该目录再拉起，
/// 只有写在那里，下一次启动才读得到同一份记录。
/// </para>
/// </remarks>
public static class CrashGuard
{
    /// <summary>统计窗口内的崩溃次数达到该值，即视为陷入重启循环。</summary>
    public const int MaxCrashesInWindow = 3;

    /// <summary>统计窗口：只关心这段时间内的崩溃，更早的视为陈旧记录。</summary>
    public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(10);

    /// <summary>存活超过该时长即认为本次启动成功，清空计数。</summary>
    public static readonly TimeSpan StableRunTime = TimeSpan.FromMinutes(5);

    private static readonly ILogger Logger = Log.ForContext(typeof(CrashGuard));

    /// <summary>崩溃路径上也会读写，进程内可能同时来自 UI 线程与后台线程，用它串行化。</summary>
    private static readonly object FileLock = new();

    /// <summary>进程内缓存，避免每次判断都读盘。</summary>
    private static long[]? _recentCrashes;

    private static string FilePath => Path.Combine(LauncherContext.RootDirectory, "Data", "CrashState.json");

    /// <summary>统计窗口内已记录的崩溃次数。</summary>
    public static int RecentCrashCount => LoadRecentCrashes().Length;

    /// <summary>是否已陷入重启循环。</summary>
    public static bool IsCrashLoopDetected() => RecentCrashCount >= MaxCrashesInWindow;

    /// <summary>
    /// 记录一次崩溃。只在确实要重启时调用，用户主动重启（托盘菜单、异常窗口按钮）不计入。
    /// </summary>
    public static void RecordCrash()
    {
        lock (FileLock)
        {
            try
            {
                var crashes = LoadRecentCrashes()
                    .Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    .ToArray();

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(crashes));

                _recentCrashes = crashes;
                Logger.Warning("已记录崩溃，{Window} 内累计 {Count}/{Max} 次",
                    CrashWindow, crashes.Length, MaxCrashesInWindow);
            }
            catch (Exception ex)
            {
                // 记不上崩溃只是让保护退化，不能反过来影响崩溃处理本身
                Logger.Error(ex, "记录崩溃失败，重启循环保护可能失效");
            }
        }
    }

    /// <summary>清空计数：本次启动已稳定运行，或属于正常退出。</summary>
    public static void Reset()
    {
        lock (FileLock)
        {
            _recentCrashes = [];

            try
            {
                File.Delete(FilePath);
                Logger.Information("已清空崩溃计数");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "清除崩溃记录失败: {FilePath}", FilePath);
            }
        }
    }

    /// <summary>读取窗口内的崩溃时间戳（Unix 毫秒）。调用方需自行加 <see cref="FileLock"/>。</summary>
    private static long[] LoadRecentCrashes()
    {
        if (_recentCrashes is null)
        {
            try
            {
                _recentCrashes = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<long[]>(File.ReadAllText(FilePath)) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                // 记录文件损坏时按没有崩溃处理，好过在崩溃路径上再抛一次
                Logger.Warning(ex, "读取崩溃记录失败，按无崩溃处理: {FilePath}", FilePath);
                _recentCrashes = [];
            }
        }

        // 丢掉窗口外的记录，避免一次陈旧的崩溃永久压制自动重启
        var cutoff = (DateTimeOffset.UtcNow - CrashWindow).ToUnixTimeMilliseconds();
        _recentCrashes = _recentCrashes.Where(timestamp => timestamp >= cutoff).ToArray();
        return _recentCrashes;
    }
}