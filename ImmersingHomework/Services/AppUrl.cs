using System.Collections.Generic;

namespace ImmersingHomework.Services;

/// <summary>解析后的应用自定义 URL。</summary>
/// <param name="Host">主机名部分，也用作路由键，例如 <c>immersinghomework://open/...</c> 中的 <c>open</c>。</param>
/// <param name="Path">主机名之后的路径分段（已做 URL 解码）。</param>
/// <param name="Query">查询参数（键值均已做 URL 解码）。</param>
public sealed record AppUrl(string Host, IReadOnlyList<string> Path, IReadOnlyDictionary<string, string> Query);
