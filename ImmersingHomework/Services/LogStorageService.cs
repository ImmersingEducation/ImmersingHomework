using System;
using System.IO;
using Serilog;

namespace ImmersingHomework.Services;

public class LogStorageService
{
    private readonly ILogger _logger = Log.ForContext<LogStorageService>();
    private readonly string _logDir;

    public LogStorageService()
    {
        _logDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
    }

    public LogStorageService(string logDir)
    {
        _logDir = logDir;
    }

    public int DeleteBefore(DateTimeOffset cutoffDate)
    {
        if (!Directory.Exists(_logDir))
            return 0;

        var deletedCount = 0;
        var cutoff = DateOnly.FromDateTime(cutoffDate.DateTime);

        foreach (var file in Directory.GetFiles(_logDir))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var lastDash = fileName.LastIndexOf('-');
            if (lastDash < 0)
                continue;

            var datePart = fileName[(lastDash + 1)..];
            var underscoreIndex = datePart.IndexOf('_');
            if (underscoreIndex >= 0)
                datePart = datePart[..underscoreIndex];

            if (datePart.Length != 8)
                continue;

            if (!DateOnly.TryParseExact(datePart, "yyyyMMdd", out var fileDate))
                continue;

            if (fileDate > cutoff)
                continue;

            try
            {
                File.Delete(file);
                deletedCount++;
                _logger.Debug("删除日志文件: {Path}", file);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "删除日志文件失败: {Path}", file);
            }
        }

        _logger.Information("共删除 {Count} 个日志文件", deletedCount);
        return deletedCount;
    }
}
