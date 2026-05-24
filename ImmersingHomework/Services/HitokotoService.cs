using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Serilog;

namespace ImmersingHomework.Services;

public static class HitokotoService
{
    private static readonly ILogger _logger = Log.ForContext(typeof(HitokotoService));
    
    public struct Hitokoto
    {
        public string Sentence { get; set; }
        public string Author { get; set; }
    }

    public static async Task<Hitokoto?> GetHitokoto()
    {
        _logger.Debug("开始获取 Hitokoto");
        try
        {
            const string url = "https://v1.hitokoto.cn";
            _logger.Debug("请求 Hitokoto API: {Url}", url);
            HttpResponseMessage response = await App.HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            _logger.Debug("Hitokoto API 响应成功，状态码: {StatusCode}", response.StatusCode);
            
            JsonNode json = JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw new InvalidOperationException();
            var hitokoto = new Hitokoto()
            {
                Sentence = Convert.ToString(json["hitokoto"]) ?? throw new InvalidOperationException(),
                Author = Convert.ToString(json["from_who"]) ?? throw new InvalidOperationException()
            };
            
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