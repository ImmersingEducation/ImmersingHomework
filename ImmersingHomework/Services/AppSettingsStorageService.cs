using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Services;

public class AppSettingsStorageService
{
    private readonly ILogger _logger = Log.ForContext<AppSettingsStorageService>();
    private string GetFilePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "Settings.json");
    }

    private string GetDataDir()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Data");
    }

    // 检查设置文件是否存在
    public bool Exists()
    {
        return File.Exists(GetFilePath());
    }

    public void Save(AppSettings settings)
    {
        _logger.Debug("正在保存应用设置");
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
        {
            _logger.Information("创建数据目录: {DataDir}", dataDir);
            Directory.CreateDirectory(dataDir);
        }
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetFilePath(), json);
        _logger.Debug("应用设置已保存");
    }

    public async Task SaveAsync(AppSettings settings)
    {
        _logger.Debug("正在异步保存应用设置");
        var dataDir = GetDataDir();
        if (!Directory.Exists(dataDir))
        {
            _logger.Information("创建数据目录: {DataDir}", dataDir);
            Directory.CreateDirectory(dataDir);
        }
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(GetFilePath(), json);
        _logger.Debug("应用设置已异步保存");
    }

    public AppSettings Load()
    {
        _logger.Debug("正在加载应用设置");
        try
        {
            var filePath = GetFilePath();
            if (!File.Exists(filePath))
            {
                _logger.Information("设置文件不存在，返回默认设置");
                return new AppSettings();
            }
            string json = File.ReadAllText(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _logger.Debug("应用设置已加载");
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "加载设置时出错，返回默认设置");
            return new AppSettings();
        }
    }
}
