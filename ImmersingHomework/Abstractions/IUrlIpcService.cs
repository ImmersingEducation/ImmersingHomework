using System;

namespace ImmersingHomework.Abstractions;

/// <summary>
/// 基于命名管道的单实例 URL 转发：已有实例作为服务端接收 URL，
/// 新启动的实例作为客户端把 URL 转发给已有实例后退出。
/// </summary>
public interface IUrlIpcService
{
    /// <summary>管道名，按当前用户派生，避免多用户环境冲突。</summary>
    string PipeName { get; }

    /// <summary>启动服务端，后台监听并回调收到的 URL。重复调用无副作用。</summary>
    void StartServer(Action<string> onUrl);

    void StopServer();

    /// <summary>把 URL 转发给已有实例。成功返回 <c>true</c>。</summary>
    bool TryForward(string url);
}