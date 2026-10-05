using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Helper;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Services;

public class UpdateService : IUpdateService
{
    private const string ReleaseCenterUrl = "http://47.122.121.60:8000";
    private const string AppName = "ImmersingHomework";

    private readonly ILogger _logger = Log.ForContext<UpdateService>();
    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return $"{version?.Major ?? 0}.{version?.Minor ?? 0}.{version?.Build ?? 0}.{version?.Revision ?? 0}";
    }

    public string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "macos";
        if (OperatingSystem.IsLinux())
            return "linux";
        return "windows";
    }

    public async Task<CheckUpdateResponse?> CheckUpdateAsync(CancellationToken ct = default)
    {
        var channel = AppSettings.Instance.UpdateChannel.Value.ToString();
        var currentVersion = GetCurrentVersion();
        var url = $"{ReleaseCenterUrl}/check/{AppName}/{channel}/{currentVersion}";

        _logger.Information("开始检查更新，当前版本: {Version}，渠道: {Channel}，地址: {Url}", currentVersion, channel, url);

        var response = await _httpClient.GetAsync(url, ct);
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
        
        if (string.IsNullOrWhiteSpace(downloadUrl))
            _logger.Information("检查更新完成，发现新版本: {Version}，但服务器并未提供平台版本的下载地址", latestVersion);

        _logger.Information("检查更新完成，发现新版本: {Version}，下载地址: {DownloadUrl}", latestVersion, downloadUrl);
        return new CheckUpdateResponse(true, latestVersion, null, downloadUrl, false);
    }

    public async Task<string?> DownloadUpdateAsync(
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
        using var response = await _httpClient.GetAsync(
            update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);

        // 先写入 .part 再改名，避免中断下载留下的半截文件被误认为完整更新包
        var partialLocation = fileLocation + ".part";
        try
        {
            await using (var fileStream = File.Create(partialLocation))
            {
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
            }

            File.Move(partialLocation, fileLocation, overwrite: true);
        }
        catch
        {
            TryDeletePartial(partialLocation);
            throw;
        }

        WriteUpdateFlag(update, fileLocation);

        _logger.Information("更新下载完成: {FileLocation}", fileLocation);
        return fileLocation;
    }

    /// <summary>
    /// 写入待应用更新标记，供 Launcher 在下次启动时识别并应用。
    /// </summary>
    /// <remarks>
    /// 标记必须写在 Launcher 读取的同一位置；未经过 Launcher 启动时该标记不会被消费，
    /// 此时直接跳过，避免遗留无人清理的文件。
    /// </remarks>
    private void WriteUpdateFlag(CheckUpdateResponse update, string fileLocation)
    {
        if (!LauncherContext.IsLaunchedByLauncher)
        {
            _logger.Information("当前不是由 Launcher 启动，无需写入更新标记");
            return;
        }

        var flagPath = LauncherContext.UpdateFlagPath;
        var content = string.Join('\n',
            update.LatestVersion ?? "unknown",
            fileLocation,
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));

        File.WriteAllText(flagPath, content);
        _logger.Information("已写入更新标记: {FlagPath}", flagPath);
    }

    private void TryDeletePartial(string partialLocation)
    {
        try
        {
            if (File.Exists(partialLocation))
                File.Delete(partialLocation);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "清理未完成的下载文件失败: {Path}", partialLocation);
        }
    }

    public string GetUpdateDirectory(string version)
    {
        return LauncherContext.GetUpdateVersionDirectory(version);
    }

    private string GetFileNameFromUrl(string url, string version)
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