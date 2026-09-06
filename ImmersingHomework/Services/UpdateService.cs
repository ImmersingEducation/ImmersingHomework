using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Services;

public record CheckUpdateResponse(
    bool HasUpdate,
    string? LatestVersion,
    string? UpdateLog,
    string? DownloadUrl,
    bool IsForceUpdate);

public static class UpdateService
{
    private static readonly ILogger _logger = Log.ForContext(typeof(UpdateService));

    private const string ReleaseCenterUrl = "http://47.122.121.60:8000";
    private const string AppName = "ImmersingHomework";

    public static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return $"{version?.Major ?? 0}.{version?.Minor ?? 0}.{version?.Build ?? 0}.{version?.Revision ?? 0}";
    }

    public static string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "macos";
        if (OperatingSystem.IsLinux())
            return "linux";
        return "windows";
    }

    public static async Task<CheckUpdateResponse?> CheckUpdateAsync(CancellationToken ct = default)
    {
        var channel = AppSettings.Instance.UpdateChannel.Value.ToString();
        var currentVersion = GetCurrentVersion();
        var url = $"{ReleaseCenterUrl}/check/{AppName}/{channel}/{currentVersion}";

        _logger.Information("开始检查更新，当前版本: {Version}，渠道: {Channel}，地址: {Url}", currentVersion, channel, url);

        HttpResponseMessage response = await App.HttpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        JsonNode json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
            ?? throw new InvalidOperationException("更新服务器返回了无效的响应");

        var updateAvailable = json["update_available"]?.GetValue<bool>() ?? false;
        if (!updateAvailable)
        {
            _logger.Information("检查更新完成，当前已是最新版本");
            return new CheckUpdateResponse(false, null, null, null, false);
        }

        var latestVersion = json["latest_version"]?.GetValue<string>();
        var downloadUrl = json["download_url"]?[GetCurrentPlatform()]?.GetValue<string>();

        _logger.Information("检查更新完成，发现新版本: {Version}，下载地址: {DownloadUrl}", latestVersion, downloadUrl);
        return new CheckUpdateResponse(true, latestVersion, null, downloadUrl, false);
    }

    public static async Task<string?> DownloadUpdateAsync(
        CheckUpdateResponse update,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (update is null)
            throw new ArgumentNullException(nameof(update));

        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            _logger.Warning("更新下载地址为空，无法下载");
            return null;
        }

        var version = update.LatestVersion ?? "unknown";
        var targetDir = GetUpdateDirectory(version);
        if (!Directory.Exists(targetDir))
        {
            _logger.Information("创建更新下载目录: {TargetDir}", targetDir);
            Directory.CreateDirectory(targetDir);
        }

        var fileName = GetFileNameFromUrl(update.DownloadUrl, version);
        var fileLocation = Path.Combine(targetDir, fileName);

        _logger.Information("开始下载更新，目标路径: {FileLocation}", fileLocation);
        using var response = await App.HttpClient.GetAsync(
            update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(fileLocation);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            if (totalBytes > 0)
                progress?.Report((double)totalRead / totalBytes * 100);
        }

        _logger.Information("更新下载完成: {FileLocation}", fileLocation);
        return fileLocation;
    }

    public static string GetUpdateDirectory(string version)
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Temp", $"Update_v{version}");
    }

    private static string GetFileNameFromUrl(string url, string version)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch (Exception e)
        {
            _logger.Warning(e, "解析下载地址文件名失败: {Url}", url);
        }

        return $"ImmersingHomework_{version}.zip";
    }
}
