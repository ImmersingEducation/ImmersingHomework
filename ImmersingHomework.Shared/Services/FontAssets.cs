using System;
using System.IO;

namespace ImmersingHomework.Services;

/// <summary>
/// 提供字体文件目录。默认指向 <c>AppContext.BaseDirectory/Assets/Fonts</c>，
/// 移动端可在启动时通过 <see cref="Directory"/> 覆盖为实际存放字体的路径。
/// </summary>
public static class FontAssets
{
    private static string? _directory;

    public static string Directory
    {
        get => _directory ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        set => _directory = value;
    }
}
