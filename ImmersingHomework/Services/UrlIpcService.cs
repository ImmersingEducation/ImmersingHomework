using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ImmersingHomework.Services;

/// <summary>
/// 基于命名管道的单实例 URL 转发：已有实例作为服务端接收 URL，
/// 新启动的实例作为客户端把 URL 转发给已有实例后退出。
/// </summary>
public static class UrlIpcService
{
    private static readonly ILogger Logger = Log.ForContext(typeof(UrlIpcService));
    private static CancellationTokenSource? _cts;

    /// <summary>每个用户独立的管道名，避免多用户环境冲突。</summary>
    public static string PipeName
    {
        get
        {
            var name = Environment.UserName ?? "user";
            var hash = 2166136261u;
            foreach (var c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return "immersinghomework-url-" + hash.ToString("X8");
        }
    }

    /// <summary>启动服务端，后台监听并回调收到的 URL。重复调用无副作用。</summary>
    public static void StartServer(Action<string> onUrl)
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServerLoop(onUrl, _cts.Token));
    }

    public static void StopServer()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>把 URL 转发给已有实例。成功返回 <c>true</c>。</summary>
    public static bool TryForward(string url)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);

            var bytes = Encoding.UTF8.GetBytes(url);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "转发 URL 到已有实例失败");
            return false;
        }
    }

    private static async Task ServerLoop(Action<string> onUrl, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var url = await reader.ReadToEndAsync(token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(url))
                {
                    Logger.Information("收到已有实例转发的 URL: {Url}", url);
                    onUrl(url);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "URL IPC 服务端异常");
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }
}
