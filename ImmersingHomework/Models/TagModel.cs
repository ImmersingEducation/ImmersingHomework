using System;
using System.Text.Json.Serialization;
using Serilog;
using Avalonia.Media;

namespace ImmersingHomework.Models;

public class TagModel
{
    private readonly ILogger _logger = Log.ForContext<TagModel>();
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Color")]
    public Color Color { get; set; }
    
    public TagModel()
    {
        _logger.Debug("TagModel 初始化");
        Color = new Color { A = 0xFF, R = 0xAD, G = 0xD8, B = 0xE6 }; // LightBlue
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