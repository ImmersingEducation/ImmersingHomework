using Avalonia;
using Avalonia.Media;
using FluentAvalonia.Styling;
using Serilog;

namespace ImmersingHomework.Helper;

/// <summary>
/// 把用户在设置里选的主题色应用到 FluentAvalonia 主题上。
/// </summary>
/// <remarks>
/// FluentAvalonia 的全部强调色画刷（<c>AccentFillColorDefaultBrush</c>、
/// <c>AccentTextFillColorPrimaryBrush</c> 等）都派生自 <c>FluentAvaloniaTheme.CustomAccentColor</c>：
/// 赋值后它会重新计算整套强调色（含深浅变体与半透明色），因此这里只需设置这一个属性，
/// 应用内所有通过 DynamicResource 引用强调色的控件都会一起变色。
/// </remarks>
public static class ThemeAccentHelper
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ThemeAccentHelper));

    /// <summary>应用当前设置里的主题色；未设置或格式非法时跟随系统强调色。</summary>
    public static void Apply(string? accentColor)
    {
        var theme = FindTheme();
        if (theme is null)
        {
            Logger.Warning("未找到 FluentAvaloniaTheme，跳过主题色设置");
            return;
        }

        theme.CustomAccentColor = Parse(accentColor);
        Logger.Information("应用主题色: {AccentColor}", theme.CustomAccentColor?.ToString() ?? "跟随系统");
    }

    /// <summary>把设置里的字符串解析成颜色；为空或非法时返回 <c>null</c>（表示跟随系统）。</summary>
    public static Color? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Color.TryParse(value, out var color))
            return color;

        Logger.Warning("主题色格式非法，改为跟随系统: {Value}", value);
        return null;
    }

    /// <summary>
    /// 把颜色转成写入设置的字符串形式，统一为 <c>#RRGGBB</c>。
    /// 主题色不保存透明度：半透明的强调色在浅色主题下几乎看不见，也几乎不会有人这么选。
    /// </summary>
    public static string ToSettingValue(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>读取 FluentAvalonia 当前生效的强调色；取不到时为 <c>null</c>。</summary>
    public static Color? GetCurrentAccentColor()
    {
        // 自定义过主题色时，资源字典里的 SystemAccentColor 仍是系统值，应以主题上的设置为准
        var custom = FindTheme()?.CustomAccentColor;
        if (custom.HasValue)
            return custom.Value;

        return Application.Current is not null &&
               Application.Current.TryGetResource("SystemAccentColor", null, out var value) &&
               value is Color systemAccent
            ? systemAccent
            : null;
    }

    private static FluentAvaloniaTheme? FindTheme()
    {
        if (Application.Current?.Styles is not { } styles)
            return null;

        foreach (var style in styles)
        {
            if (style is FluentAvaloniaTheme theme)
                return theme;
        }

        return null;
    }
}
