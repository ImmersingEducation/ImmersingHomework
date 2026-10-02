using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services;

public class UrlIpcService : IUrlIpcService
{
    private readonly ILogger _logger = Log.ForContext<UrlIpcService>();
    private CancellationTokenSource? _cts;

    public string PipeName
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

    public void StartServer(Action<string> onUrl)
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServerLoop(onUrl, _cts.Token));
    }

    public void StopServer()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public bool TryForward(string url)
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
            _logger.Warning(ex, "转发 URL 到已有实例失败");
            return false;
        }
    }

    private async Task ServerLoop(Action<string> onUrl, CancellationToken token)
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
                    _logger.Information("收到已有实例转发的 URL: {Url}", url);
                    onUrl(url);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "URL IPC 服务端异常");
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }
}