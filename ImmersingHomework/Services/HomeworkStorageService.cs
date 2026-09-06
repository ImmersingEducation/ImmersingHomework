using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Services;

public class HomeworkStorageService
{
    private readonly ILogger _logger = Log.ForContext<HomeworkStorageService>();
    private string GetFilePath(DateOnly date)
    {
        return Path.Combine(
            Directory.GetCurrentDirectory(), 
            "Data", 
            "Homeworks", 
            $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}.json"
        );
    }

    private string GetDataDir()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "Homeworks");
    }

    private string GetNextSnapshotPath(DateOnly date)
    {
        var dataDir = GetDataDir();
        var prefix = $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}_snapshot-";
        var max = 0;
        foreach (var file in Directory.GetFiles(dataDir, $"{prefix}*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var number = fileName[prefix.Length..];
            if (int.TryParse(number, out var n) && n > max)
                max = n;
        }
        return Path.Combine(dataDir, $"{prefix}{max + 1}.json");
    }

    private void SaveSnapshot(DateOnly date)
    {
        var filePath = GetFilePath(date);
        if (!File.Exists(filePath))
            return;

        var snapshotPath = GetNextSnapshotPath(date);
        _logger.Information("保存作业快照，日期: {Date}，快照文件: {Snapshot}", date, snapshotPath);
        File.Copy(filePath, snapshotPath);
    }

    // 检查指定日期的作业文件是否存在
    public bool Exists(DateOnly date)
    {
        return File.Exists(GetFilePath(date));
    }

    // 相比已保存的档案，仅 Frozen 属性发生变化时不视为内容变更
    private bool HasOnlyFrozenChanged(Homework homework)
    {
        var filePath = GetFilePath(homework.Date);
        if (!File.Exists(filePath))
            return false;

        try
        {
            var existing = JsonNode.Parse(File.ReadAllText(filePath))?.AsObject();
            var updated = JsonNode.Parse(JsonSerializer.Serialize(homework))?.AsObject();
            if (existing is null || updated is null)
                return false;

            existing.Remove("Frozen");
            updated.Remove("Frozen");
            return JsonNode.DeepEquals(existing, updated);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "比较作业内容时出错，日期: {Date}", homework.Date);
            return false;
        }
    }

    public void Save(Homework homework)
    {
        _logger.Debug("正在保存作业，日期: {Date}", homework.Date);
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
        {
            _logger.Information("创建作业数据目录: {DataDir}", dataDir);
            Directory.CreateDirectory(dataDir);
        }
        if (!HasOnlyFrozenChanged(homework))
            SaveSnapshot(homework.Date);
        string json = JsonSerializer.Serialize(homework, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetFilePath(homework.Date), json);
        _logger.Debug("作业已保存，日期: {Date}", homework.Date);
    }

    public async Task SaveAsync(Homework homework)
    {
        _logger.Debug("正在异步保存作业，日期: {Date}", homework.Date);
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
        {
            _logger.Information("创建作业数据目录: {DataDir}", dataDir);
            Directory.CreateDirectory(dataDir);
        }
        if (!HasOnlyFrozenChanged(homework))
            SaveSnapshot(homework.Date);
        string json = JsonSerializer.Serialize(homework, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetFilePath(homework.Date), json);
        _logger.Debug("作业已异步保存，日期: {Date}", homework.Date);
    }

    public Homework? Load(DateOnly date)
    {
        _logger.Debug("正在加载作业，日期: {Date}", date);
        try
        {
            var filePath = GetFilePath(date);
            if (!File.Exists(filePath))
            {
                _logger.Information("作业文件不存在，返回空作业，日期: {Date}", date);
                return new Homework(date, []);
            }
            string json = File.ReadAllText(filePath);
            var homework = JsonSerializer.Deserialize<Homework>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _logger.Debug("作业已加载，日期: {Date}", date);
            return homework;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "加载作业时出错，返回空作业，日期: {Date}", date);
            return new Homework(date, []);
        }
    }

    public async Task<Homework?> LoadAsync(DateOnly date)
    {
        _logger.Debug("正在异步加载作业，日期: {Date}", date);
        try
        {
            var filePath = GetFilePath(date);
            if (!File.Exists(filePath))
            {
                _logger.Information("作业文件不存在，返回空作业，日期: {Date}", date);
                return new Homework(date, []);
            }
            string json = await File.ReadAllTextAsync(filePath);
            var homework = JsonSerializer.Deserialize<Homework>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _logger.Debug("作业已异步加载，日期: {Date}", date);
            return homework;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "异步加载作业时出错，返回空作业，日期: {Date}", date);
            return new Homework(date, []);
        }
    }

    public Homework? LoadFromFile(string filePath)
    {
        _logger.Debug("正在从文件加载作业: {Path}", filePath);
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.Warning("作业文件不存在: {Path}", filePath);
                return null;
            }
            string json = File.ReadAllText(filePath);
            var homework = JsonSerializer.Deserialize<Homework>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _logger.Debug("作业已从文件加载: {Path}", filePath);
            return homework;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "从文件加载作业时出错: {Path}", filePath);
            return null;
        }
    }

    public List<DateOnly> GetAllHomeworkDates()
    {
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
            return [];

        var dates = new List<DateOnly>();
        foreach (var file in Directory.GetFiles(dataDir, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (DateOnly.TryParse(fileName, out var date))
                dates.Add(date);
        }
        return dates;
    }

    public bool Delete(DateOnly date)
    {
        var filePath = GetFilePath(date);
        if (!File.Exists(filePath))
            return false;

        _logger.Information("删除作业文件，日期: {Date}", date);
        File.Delete(filePath);
        return true;
    }

    public int DeleteBeforeAndEmpty(DateTimeOffset cutoffDate)
    {
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
            return 0;

        var deletedCount = 0;
        var cutoff = DateOnly.FromDateTime(cutoffDate.DateTime);

        foreach (var file in Directory.GetFiles(dataDir, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!DateOnly.TryParse(fileName, out var date))
                continue;

            bool shouldDelete = date <= cutoff;

            if (!shouldDelete)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var homework = JsonSerializer.Deserialize<Homework>(content,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (homework is { HomeworkItems.Count: 0 })
                        shouldDelete = true;
                }
                catch
                {
                    // corrupted file, delete it
                    shouldDelete = true;
                }
            }

            if (shouldDelete)
            {
                _logger.Information("删除作业文件，日期: {Date}", date);
                File.Delete(file);
                deletedCount++;
            }
        }

        _logger.Information("共删除 {Count} 个作业文件", deletedCount);
        return deletedCount;
    }
}