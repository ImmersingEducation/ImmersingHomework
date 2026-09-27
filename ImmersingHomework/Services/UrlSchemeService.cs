using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ImmersingHomework.Services;

public static class UrlSchemeService
{
    public const string Scheme = "immersinghomework";
    public const string Prefix = Scheme + "://";

    private static readonly ILogger Logger = Log.ForContext(typeof(UrlSchemeService));
    private static readonly Dictionary<string, Action<AppUrl>> Routes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> StartupUrls = [];

    /// <summary>注册一个路由，路由键为 URL 的主机名部分（不区分大小写）。</summary>
    public static void RegisterRoute(string route, Action<AppUrl> handler) => Routes[route] = handler;

    /// <summary>从命令行参数中提取所有以 <c>immersinghomework://</c> 开头的 URL。</summary>
    public static IEnumerable<string> ExtractUrls(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                yield return arg;
        }
    }

    /// <summary>把启动时收到的 URL 暂存起来，待 UI 就绪后再统一处理。</summary>
    public static void EnqueueStartupUrl(string raw) => StartupUrls.Add(raw);

    /// <summary>处理所有暂存的启动 URL（应在 UI 线程调用）。</summary>
    public static void FlushStartupUrls()
    {
        var urls = StartupUrls.ToArray();
        StartupUrls.Clear();
        foreach (var url in urls) Handle(url);
    }

    public static bool TryParse(string? raw, out AppUrl url)
    {
        url = new AppUrl(string.Empty, Array.Empty<string>(), new Dictionary<string, string>());
        if (string.IsNullOrWhiteSpace(raw)) return false;

        raw = raw.Trim();
        if (!raw.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = raw[Prefix.Length..];

        string pathPart;
        string? queryPart = null;
        var queryIndex = rest.IndexOf('?');
        if (queryIndex >= 0)
        {
            pathPart = rest[..queryIndex];
            queryPart = rest[(queryIndex + 1)..];
        }
        else
        {
            pathPart = rest;
        }

        var segments = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var host = segments.Length > 0 ? Uri.UnescapeDataString(segments[0]) : string.Empty;
        var path = segments.Skip(1).Select(Uri.UnescapeDataString).ToArray();

        var query = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(queryPart))
        {
            foreach (var pair in queryPart.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = eq >= 0 ? pair[..eq] : pair;
                var value = eq >= 0 ? pair[(eq + 1)..] : string.Empty;
                query[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
            }
        }

        url = new AppUrl(host, path, query);
        return true;
    }

    /// <summary>解析并分发一个 URL。应在 UI 线程调用。</summary>
    public static void Handle(string raw)
    {
        if (!TryParse(raw, out var url))
        {
            Logger.Warning("无法解析 URL: {Raw}", raw);
            return;
        }

        if (Routes.TryGetValue(url.Host, out var handler))
        {
            Logger.Information("分发 URL 路由: {Host}", url.Host);
            handler(url);
        }
        else
        {
            Logger.Warning("未注册的 URL 路由: {Host}", url.Host);
        }
    }
}
