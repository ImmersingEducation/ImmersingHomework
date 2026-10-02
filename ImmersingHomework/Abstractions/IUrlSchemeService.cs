using System;
using System.Collections.Generic;
using ImmersingHomework.Services;

namespace ImmersingHomework.Abstractions;

/// <summary>
/// <c>immersinghomework://</c> 自定义 URL 的解析与路由分发。
/// </summary>
public interface IUrlSchemeService
{
    /// <summary>注册一个路由，路由键为 URL 的主机名部分（不区分大小写）。</summary>
    void RegisterRoute(string route, Action<AppUrl> handler);

    /// <summary>从命令行参数中提取所有以 <c>immersinghomework://</c> 开头的 URL。</summary>
    IEnumerable<string> ExtractUrls(IEnumerable<string> args);

    /// <summary>把启动时收到的 URL 暂存起来，待 UI 就绪后再统一处理。</summary>
    void EnqueueStartupUrl(string raw);

    /// <summary>处理所有暂存的启动 URL（应在 UI 线程调用）。</summary>
    void FlushStartupUrls();

    bool TryParse(string? raw, out AppUrl url);

    /// <summary>解析并分发一个 URL（应在 UI 线程调用）。</summary>
    void Handle(string raw);
}