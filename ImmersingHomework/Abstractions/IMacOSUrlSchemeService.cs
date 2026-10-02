using System;

namespace ImmersingHomework.Abstractions;

/// <summary>macOS 上通过 Apple Event 接收 <c>immersinghomework://</c> URL。</summary>
public interface IMacOSUrlSchemeService
{
    /// <summary>安装 kAEGetURL 事件处理器，收到 URL 时回调 <paramref name="onUrl"/>。</summary>
    void Install(Action<string> onUrl);
}