using System.Diagnostics;
using System.IO.Compression;

namespace ImmersingHomework.Launcher;

class Program
{
    private const string MainAppFolder = "ImmersingHomework";
    private const string MainAppName = "ImmersingHomework";
    private const string UpdateRootFolder = "Temp";
    private const string UpdateFlagFileName = "update.flag";

    /// <summary>
    /// 主程序下载未完成时使用的临时后缀。应用更新时需跳过这些残留文件。
    /// </summary>
    private const string PartialDownloadSuffix = ".part";

    /// <summary>
    /// 传给子进程的环境变量名。主程序据此定位更新暂存目录、写入更新标记，
    /// 并在重启时回到本 Launcher，使待应用的更新真正生效。
    /// 需与 ImmersingHomework.Helper.LauncherContext.DirectoryVariableName 保持一致。
    /// </summary>
    private const string LauncherDirectoryVariableName = "IMMERSINGHOMEWORK_LAUNCHER_DIR";

    /// <summary>
    /// 主程序的单实例锁文件路径，与 ImmersingHomework.Program.Main 中的约定保持一致。
    /// 该文件被主程序以 <see cref="FileShare.None"/> 独占打开，可用来判断主程序是否仍在运行。
    /// </summary>
    private static string InstanceLockFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ImmersingHomework", "instance.lock");

    static int Main(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var flagPath = Path.Combine(baseDir, UpdateFlagFileName);

        if (File.Exists(flagPath))
        {
            ApplyUpdates(baseDir, flagPath);
        }

        var mainFolder = Path.Combine(baseDir, MainAppFolder);
        var mainExe = Path.Combine(mainFolder, OperatingSystem.IsWindows()
            ? $"{MainAppName}.exe"
            : MainAppName);

        if (!File.Exists(mainExe))
        {
            var message = $"未找到主程序: {mainExe}";
            Diagnostics.Error(message);
            Diagnostics.ShowError(message);
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = mainExe,
            WorkingDirectory = baseDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // 告知子进程 Launcher 目录：更新包暂存目录与 update.flag 都以该目录为根，
        // 与本进程读取 update.flag 的位置一致。
        startInfo.Environment[LauncherDirectoryVariableName] = baseDir;

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        process?.WaitForExit();
        return process?.ExitCode ?? 0;
    }

    private static void ApplyUpdates(string baseDir, string flagPath)
    {
        // 主程序仍在运行时其程序集被占用，覆盖会导致运行中的实例崩溃或安装目录处于不一致状态。
        // 保留 update.flag，等待用户真正退出后再应用。
        if (IsMainAppRunning())
        {
            Diagnostics.Info("主程序正在运行，跳过应用更新，将在下次启动时应用");
            return;
        }

        var tempDir = Path.Combine(baseDir, UpdateRootFolder);
        var mainDir = Path.Combine(baseDir, MainAppFolder);

        try
        {
            var applied = 0;
            if (Directory.Exists(tempDir))
            {
                foreach (var updateDir in Directory.GetDirectories(tempDir))
                {
                    var zipFiles = Directory.GetFiles(updateDir, "*.zip", SearchOption.TopDirectoryOnly);
                    if (zipFiles.Length > 0)
                    {
                        foreach (var zipFile in zipFiles)
                        {
                            var extractDir = Path.Combine(updateDir,
                                $"_{Path.GetFileNameWithoutExtension(zipFile)}_extracted");
                            if (Directory.Exists(extractDir))
                                Directory.Delete(extractDir, true);
                            ZipFile.ExtractToDirectory(zipFile, extractDir);
                            CopyDirectoryContents(extractDir, mainDir);
                        }
                    }
                    else
                    {
                        CopyDirectoryContents(updateDir, mainDir);
                    }

                    Directory.Delete(updateDir, true);
                    applied++;
                    Diagnostics.Info($"已应用更新: {Path.GetFileName(updateDir)}");
                }
            }

            // 只有确实应用了更新才删除标记，否则残留标记会让每次启动都重试空目录。
            if (applied > 0)
                File.Delete(flagPath);
        }
        catch (Exception ex)
        {
            var message = $"应用更新失败: {ex.Message}";
            Diagnostics.Error(message);
            Diagnostics.ShowWarning(message);
        }
    }

    /// <summary>
    /// 通过尝试独占打开主程序的实例锁文件判断其是否正在运行。
    /// 未能打开（含目录不存在等异常）时按“正在运行”处理，宁可推迟应用也不冒险覆盖。
    /// </summary>
    private static bool IsMainAppRunning()
    {
        var lockPath = InstanceLockFilePath;
        if (!File.Exists(lockPath))
            return false;

        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// 先把源目录写入同级临时目录，成功后再替换目标目录，避免拷贝中断留下半新半旧的安装。
    /// </summary>
    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        var stagingDir = $"{destDir}.new";

        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, true);

        CopyInto(sourceDir, stagingDir);

        // 目标目录整体换出后再改名，File.Move 对已存在的目录会失败，故先移除旧目录
        var backupDir = $"{destDir}.old";
        if (Directory.Exists(backupDir))
            Directory.Delete(backupDir, true);

        var hadPrevious = Directory.Exists(destDir);
        if (hadPrevious)
            Directory.Move(destDir, backupDir);

        try
        {
            Directory.Move(stagingDir, destDir);
        }
        catch
        {
            // 换入失败则把旧目录换回，避免安装目录消失
            if (hadPrevious && Directory.Exists(backupDir) && !Directory.Exists(destDir))
                Directory.Move(backupDir, destDir);
            throw;
        }

        if (Directory.Exists(backupDir))
            Directory.Delete(backupDir, true);
    }

    private static void CopyInto(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            // 未完成的下载残留物，不能覆盖安装目录
            if (file.EndsWith(PartialDownloadSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyInto(subDir, destSubDir);
        }
    }
}
