using System.Diagnostics;
using System.IO.Compression;

namespace ImmersingHomework.Launcher;

class Program
{
    private const string MainAppFolder = "ImmersingHomework";
    private const string MainAppName = "ImmersingHomework";
    private const string UpdateRootFolder = "Temp";
    private const string UpdateFlagFileName = "update.flag";

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
            Console.Error.WriteLine($"未找到主程序: {mainExe}");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = mainExe,
            WorkingDirectory = baseDir,
            UseShellExecute = false,
        };

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
        var tempDir = Path.Combine(baseDir, UpdateRootFolder);
        var mainDir = Path.Combine(baseDir, MainAppFolder);

        try
        {
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
                            ZipFile.ExtractToDirectory(zipFile, extractDir);
                            CopyDirectoryContents(extractDir, mainDir);
                        }
                    }
                    else
                    {
                        CopyDirectoryContents(updateDir, mainDir);
                    }

                    Directory.Delete(updateDir, true);
                    Console.WriteLine($"已应用更新: {Path.GetFileName(updateDir)}");
                }
            }

            File.Delete(flagPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"应用更新失败: {ex.Message}");
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryContents(subDir, destSubDir);
        }
    }
}
