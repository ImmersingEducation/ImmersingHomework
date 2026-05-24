using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ImmersingHomework.Models;
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

    // 检查指定日期的作业文件是否存在
    public bool Exists(DateOnly date)
    {
        return File.Exists(GetFilePath(date));
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
}