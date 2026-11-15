using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Serilog;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaColor = Avalonia.Media.Color;

namespace ImmersingHomework.Shared.Models;

public class TagModel
{
    private readonly ILogger _logger = Log.ForContext<TagModel>();
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 标签颜色的标识符。持久化的始终是浅色配色，深色模式显示时由
    /// <see cref="TagColorPalette"/> 映射到对应的深色配色，不修改此属性。
    /// </summary>
    [JsonPropertyName("Color")]
    public Color Color { get; set; }
    
    public TagModel()
    {
        _logger.Debug("TagModel 初始化");
        Color = new Color { A = 0xFF, R = 0xAD, G = 0xD8, B = 0xE6 }; // LightBlue
    }

    /// <summary>
    /// 获取当前主题下用于显示的标签颜色。
    /// </summary>
    public AvaloniaColor GetDisplayColor(ThemeVariant themeVariant)
    {
        return TagColorPalette.Resolve(Color.ToAvaloniaColor(), themeVariant);
    }

    /// <summary>
    /// 获取当前主题下用于显示的标签画刷。
    /// </summary>
    public SolidColorBrush GetDisplayBrush(ThemeVariant themeVariant)
    {
        return new SolidColorBrush(GetDisplayColor(themeVariant));
    }
}

/// <summary>
/// 标签显示配色。浅色配色作为持久化标识符，深色模式使用这里定义的对应深色配色。
/// </summary>
public static class TagColorPalette
{
    private static readonly IReadOnlyDictionary<AvaloniaColor, AvaloniaColor> DarkColors =
        new Dictionary<AvaloniaColor, AvaloniaColor>
        {
            [AvaloniaColor.FromRgb(0xAD, 0xD8, 0xE6)] = AvaloniaColor.FromRgb(0x1F, 0x4A, 0x5C), // 默认浅蓝
            [AvaloniaColor.FromRgb(0xDC, 0xF0, 0xFF)] = AvaloniaColor.FromRgb(0x1F, 0x4E, 0x6B), // 蓝色
            [AvaloniaColor.FromRgb(0xFF, 0xF0, 0xDC)] = AvaloniaColor.FromRgb(0x6B, 0x47, 0x22), // 橙色
            [AvaloniaColor.FromRgb(0xDC, 0xFF, 0xDC)] = AvaloniaColor.FromRgb(0x2A, 0x5C, 0x36), // 绿色
            [AvaloniaColor.FromRgb(0xFF, 0xDC, 0xF0)] = AvaloniaColor.FromRgb(0x66, 0x33, 0x4E), // 粉色
            [AvaloniaColor.FromRgb(0xF0, 0xF0, 0xDC)] = AvaloniaColor.FromRgb(0x61, 0x5A, 0x2A), // 黄色
            [AvaloniaColor.FromRgb(0xF0, 0xDC, 0xFF)] = AvaloniaColor.FromRgb(0x4B, 0x32, 0x68), // 紫色
            [AvaloniaColor.FromRgb(0xFF, 0xDC, 0xDC)] = AvaloniaColor.FromRgb(0x6B, 0x30, 0x30), // 红色
            [AvaloniaColor.FromRgb(0xDC, 0xFF, 0xF0)] = AvaloniaColor.FromRgb(0x23, 0x5C, 0x53)  // 青色
        };

    /// <summary>
    /// 将标签颜色标识符解析为当前主题下的显示颜色。未收录的自定义颜色保持原样。
    /// </summary>
    public static AvaloniaColor Resolve(AvaloniaColor identifier, ThemeVariant themeVariant)
    {
        if (themeVariant != ThemeVariant.Dark)
            return identifier;

        return DarkColors.TryGetValue(identifier, out var darkColor) ? darkColor : identifier;
    }

    /// <summary>
    /// 判断颜色是否足够浅，以便在其上使用深色文字。
    /// </summary>
    public static bool IsLight(AvaloniaColor color)
    {
        return 0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 150;
    }

    /// <summary>
    /// 获取与指定背景色对比度足够的前景色。
    /// </summary>
    public static AvaloniaColor GetContrastingForeground(AvaloniaColor background)
    {
        return IsLight(background) ? Colors.Black : Colors.White;
    }

    /// <summary>
    /// 在无法确定背景色时，按主题获取默认前景色。
    /// </summary>
    public static AvaloniaColor GetContrastingForeground(ThemeVariant themeVariant)
    {
        return themeVariant == ThemeVariant.Dark ? Colors.White : Colors.Black;
    }
}

public struct Color
{
    public byte A { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public Avalonia.Media.Color ToAvaloniaColor()
    {
        return Avalonia.Media.Color.FromArgb(A, R, G, B);
    }

    public SolidColorBrush ToSolidColorBrush()
    {
        return new SolidColorBrush(ToAvaloniaColor());
    }

    public static Color FromAvaloniaColor(Avalonia.Media.Color color)
    {
        return new Color { A = color.A, R = color.R, G = color.G, B = color.B };
    }

    public static implicit operator Avalonia.Media.Color(Color color)
    {
        return color.ToAvaloniaColor();
    }

    public static implicit operator Color(Avalonia.Media.Color color)
    {
        return FromAvaloniaColor(color);
    }
}
