using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Services;

public class HitokotoService : IHitokotoService
{
    private const string HitokotoApiUrl = "https://v1.hitokoto.cn";

    private readonly ILogger _logger = Log.ForContext<HitokotoService>();
    private readonly HttpClient _httpClient;

    public HitokotoService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Hitokoto?> GetHitokoto()
    {
        _logger.Debug("开始获取 Hitokoto");
        try
        {
            _logger.Debug("请求 Hitokoto API: {Url}", HitokotoApiUrl);
            var response = await _httpClient.GetAsync(HitokotoApiUrl);
            response.EnsureSuccessStatusCode();
            _logger.Debug("Hitokoto API 响应成功，状态码: {StatusCode}", response.StatusCode);

            JsonNode json = JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw new InvalidOperationException();
            var hitokoto = new Hitokoto(
                Convert.ToString(json["hitokoto"]) ?? throw new InvalidOperationException(),
                Convert.ToString(json["from_who"]) ?? throw new InvalidOperationException());

            _logger.Debug("成功获取 Hitokoto: {Sentence} —— {Author}", hitokoto.Sentence, hitokoto.Author);
            return hitokoto;
        }
        catch (Exception e)
        {
            _logger.Error(e, "获取 Hitokoto 失败");
            return null;
        }
    }
}