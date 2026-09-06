using System;
using System.IO;
using Serilog;

namespace ImmersingHomework.Services;

public class OutputStorageService
{
    private readonly ILogger _logger = Log.ForContext<OutputStorageService>();
    private readonly string _outputDir;

    public OutputStorageService()
    {
        _outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
    }

    public OutputStorageService(string outputDir)
    {
        _outputDir = outputDir;
    }

    public int DeleteBefore(DateTimeOffset cutoffDate)
    {
        if (!Directory.Exists(_outputDir))
            return 0;

        var deletedCount = 0;
        var cutoff = DateOnly.FromDateTime(cutoffDate.DateTime);

        foreach (var file in Directory.GetFiles(_outputDir))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var underscoreIndex = fileName.IndexOf('_');
            if (underscoreIndex <= 0)
                continue;

            var datePart = fileName[..underscoreIndex];
            if (!DateOnly.TryParse(datePart, out var fileDate))
                continue;

            if (fileDate > cutoff)
                continue;

            try
            {
                File.Delete(file);
                deletedCount++;
                _logger.Debug("删除导出文件: {Path}", file);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "删除导出文件失败: {Path}", file);
            }
        }

        _logger.Information("共删除 {Count} 个导出文件", deletedCount);
        return deletedCount;
    }
}
