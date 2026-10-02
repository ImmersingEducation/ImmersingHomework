using System;
using System.Collections.Generic;
using System.Linq;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services;

public class UrlSchemeService : IUrlSchemeService
{
    public const string Scheme = "immersinghomework";
    public const string Prefix = Scheme + "://";

    private readonly ILogger _logger = Log.ForContext<UrlSchemeService>();
    private readonly Dictionary<string, Action<AppUrl>> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _startupUrls = [];

    public void RegisterRoute(string route, Action<AppUrl> handler) => _routes[route] = handler;

    public IEnumerable<string> ExtractUrls(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                yield return arg;
        }
    }

    public void EnqueueStartupUrl(string raw) => _startupUrls.Add(raw);

    public void FlushStartupUrls()
    {
        var urls = _startupUrls.ToArray();
        _startupUrls.Clear();
        foreach (var url in urls) Handle(url);
    }

    public bool TryParse(string? raw, out AppUrl url)
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

    public void Handle(string raw)
    {
        if (!TryParse(raw, out var url))
        {
            _logger.Warning("无法解析 URL: {Raw}", raw);
            return;
        }

        if (_routes.TryGetValue(url.Host, out var handler))
        {
            _logger.Information("分发 URL 路由: {Host}", url.Host);
            handler(url);
        }
        else
        {
            _logger.Warning("未注册的 URL 路由: {Host}", url.Host);
        }
    }
}