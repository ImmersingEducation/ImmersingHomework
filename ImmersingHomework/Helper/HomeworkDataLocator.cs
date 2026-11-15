using System;
using System.Collections.Generic;
using System.IO;

namespace ImmersingHomework.Helper;

/// <summary>
/// 定位作业数据目录与作业文件。
/// </summary>
/// <remarks>
/// 纯查询路由（<c>immersinghomework://homework</c>）可能由系统从任意工作目录唤起，
/// 此时不能像常规运行那样依赖 <see cref="Directory.GetCurrentDirectory()"/>。
/// 这里按「Launcher 发布目录 → 主程序所在目录的父目录 → 主程序所在目录 → 当前工作目录」
/// 的顺序查找实际存在的数据，使查询结果与用户日常使用时看到的数据一致。
/// </remarks>
public static class HomeworkDataLocator
{
    private const string DataFolderName = "Data";
    private const string HomeworkFolderName = "Homeworks";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>返回包含指定日期作业文件的路径；文件不存在时返回首选数据目录下的路径。</summary>
    public static string ResolveFilePath(DateOnly date)
    {
        var fileName = $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}.json";

        foreach (var dataDirectory in EnumerateDataDirectories())
        {
            var filePath = Path.Combine(dataDirectory, fileName);
            if (File.Exists(filePath))
                return filePath;
        }

        return Path.Combine(ResolveDataDirectory(), fileName);
    }

    /// <summary>返回首选作业数据目录（<c>&lt;根目录&gt;/Data/Homeworks</c>）。</summary>
    public static string ResolveDataDirectory()
    {
        string? firstCandidate = null;

        foreach (var dataDirectory in EnumerateDataDirectories())
        {
            firstCandidate ??= dataDirectory;
            if (Directory.Exists(dataDirectory))
                return dataDirectory;
        }

        return firstCandidate ?? Path.Combine(Directory.GetCurrentDirectory(), DataFolderName, HomeworkFolderName);
    }

    private static IEnumerable<string> EnumerateDataDirectories()
    {
        foreach (var root in EnumerateRootCandidates())
            yield return Path.Combine(root, DataFolderName, HomeworkFolderName);
    }

    private static IEnumerable<string> EnumerateRootCandidates()
    {
        var seen = new HashSet<string>(PathComparer);
        var candidates = new List<string>(4);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            if (seen.Add(fullPath))
                candidates.Add(fullPath);
        }

        // 由 Launcher 启动时，环境变量给出的发布目录就是数据根目录
        if (LauncherContext.IsLaunchedByLauncher)
            Add(LauncherContext.RootDirectory);

        // 发布产物中主程序位于 <发布目录>/ImmersingHomework/，数据则在 <发布目录>/Data/，
        // 因此优先检查主程序所在目录的父目录，避免主程序目录内的遗留数据抢先命中。
        var baseDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Add(Path.GetDirectoryName(baseDirectory));
        Add(baseDirectory);

        // 开发调试或从数据所在目录启动时回退到当前工作目录
        Add(Directory.GetCurrentDirectory());

        return candidates;
    }
}
