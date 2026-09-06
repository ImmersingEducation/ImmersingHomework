using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace ImmersingHomework.Services;

public record SnapshotInfo(string FilePath, DateTime CreatedAt);

public class SnapshotStorageService
{
    private readonly ILogger _logger = Log.ForContext<SnapshotStorageService>();

    private string GetDataDir()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "Homeworks");
    }

    public List<SnapshotInfo> GetSnapshots(DateOnly date)
    {
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
            return [];

        var prefix = $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}_snapshot-";
        var snapshots = new List<SnapshotInfo>();
        foreach (var file in Directory.GetFiles(dataDir, $"{prefix}*.json"))
        {
            var createdAt = File.GetCreationTime(file);
            snapshots.Add(new SnapshotInfo(file, createdAt));
        }

        snapshots.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        _logger.Debug("获取到 {Count} 个快照，日期: {Date}", snapshots.Count, date);
        return snapshots;
    }

    private static bool IsSnapshotFile(string fileName)
    {
        return fileName.Contains("_snapshot-", StringComparison.Ordinal)
               && fileName.EndsWith(".json", StringComparison.Ordinal);
    }

    private static bool TryGetSnapshotDate(string fileName, out DateOnly date)
    {
        date = default;
        var underscoreIndex = fileName.IndexOf('_');
        if (underscoreIndex <= 0)
            return false;

        var datePart = fileName[..underscoreIndex];
        return DateOnly.TryParse(datePart, out date);
    }

    public long GetStorageUsage()
    {
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
            return 0;

        long totalBytes = 0;
        foreach (var file in Directory.GetFiles(dataDir, "*.json"))
        {
            var fileName = Path.GetFileName(file);
            if (!IsSnapshotFile(fileName))
                continue;

            try
            {
                totalBytes += new FileInfo(file).Length;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "获取快照文件大小时出错: {Path}", file);
            }
        }

        _logger.Debug("快照存储空间占用: {Bytes} 字节", totalBytes);
        return totalBytes;
    }

    public int ClearAll()
    {
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
            return 0;

        var deletedCount = 0;
        foreach (var file in Directory.GetFiles(dataDir, "*.json"))
        {
            var fileName = Path.GetFileName(file);
            if (!IsSnapshotFile(fileName))
                continue;

            try
            {
                File.Delete(file);
                deletedCount++;
                _logger.Debug("删除快照文件: {Path}", file);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "删除快照文件失败: {Path}", file);
            }
        }

        _logger.Information("共删除 {Count} 个快照文件", deletedCount);
        return deletedCount;
    }

    public int ClearBefore(DateTimeOffset cutoffDate)
    {
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
            return 0;

        var deletedCount = 0;
        var cutoff = DateOnly.FromDateTime(cutoffDate.DateTime);

        foreach (var file in Directory.GetFiles(dataDir, "*.json"))
        {
            var fileName = Path.GetFileName(file);
            if (!IsSnapshotFile(fileName))
                continue;

            if (!TryGetSnapshotDate(fileName, out var date) || date > cutoff)
                continue;

            try
            {
                File.Delete(file);
                deletedCount++;
                _logger.Debug("删除快照文件: {Path}", file);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "删除快照文件失败: {Path}", file);
            }
        }

        _logger.Information("共删除 {Count} 个快照文件", deletedCount);
        return deletedCount;
    }
}
