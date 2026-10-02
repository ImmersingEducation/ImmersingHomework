using System;
using System.Threading;
using System.Threading.Tasks;
using ImmersingHomework.Models;

namespace ImmersingHomework.Abstractions;

/// <summary>应用更新检查与更新包下载。</summary>
public interface IUpdateService
{
    string GetCurrentVersion();

    string GetCurrentPlatform();

    Task<CheckUpdateResponse?> CheckUpdateAsync(CancellationToken ct = default);

    /// <summary>下载更新包，返回下载后的本地文件路径；下载地址为空时返回 <c>null</c>。</summary>
    Task<string?> DownloadUpdateAsync(CheckUpdateResponse update, IProgress<double>? progress = null,
        CancellationToken ct = default);

    string GetUpdateDirectory(string version);
}