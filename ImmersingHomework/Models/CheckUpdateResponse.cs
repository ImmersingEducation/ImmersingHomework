namespace ImmersingHomework.Models;

/// <summary>更新服务器的检查结果。</summary>
/// <param name="HasUpdate">是否存在新版本。</param>
/// <param name="LatestVersion">最新版本号。</param>
/// <param name="UpdateLog">更新日志。</param>
/// <param name="DownloadUrl">当前平台的更新包下载地址。</param>
/// <param name="IsForceUpdate">是否为强制更新。</param>
public record CheckUpdateResponse(
    bool HasUpdate,
    string? LatestVersion,
    string? UpdateLog,
    string? DownloadUrl,
    bool IsForceUpdate);