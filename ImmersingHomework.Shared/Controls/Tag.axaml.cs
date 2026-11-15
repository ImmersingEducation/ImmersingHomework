using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Shared.Controls;

public partial class Tag : UserControl
{
    private readonly ILogger _logger = Log.ForContext<Tag>();
    public static readonly StyledProperty<string> TagNameProperty =
        AvaloniaProperty.Register<Tag, string>(nameof(TagName));

    public static readonly StyledProperty<IBrush> TagColorProperty =
        AvaloniaProperty.Register<Tag, IBrush>(nameof(TagColor), new SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 240, 255)));

    /// <summary>
    /// 是否根据当前主题把 <see cref="TagColor"/> 作为浅色标识符映射为实际显示颜色。
    /// 打印、回执等固定浅色背景的场景可设为 <c>false</c>。
    /// </summary>
    public static readonly StyledProperty<bool> UseThemeVariantColorProperty =
        AvaloniaProperty.Register<Tag, bool>(nameof(UseThemeVariantColor), true);

    static Tag()
    {
        TagColorProperty.Changed.AddClassHandler<Tag>((tag, _) => tag.UpdateTagAppearance());
        UseThemeVariantColorProperty.Changed.AddClassHandler<Tag>((tag, _) => tag.UpdateTagAppearance());
    }

    public string TagName
    {
        get => GetValue(TagNameProperty);
        set 
        { 
            SetValue(TagNameProperty, value);
            if (TagText != null)
                TagText.Text = value;
        }
    }

    public IBrush TagColor
    {
        get => GetValue(TagColorProperty);
        set => SetValue(TagColorProperty, value);
    }

    public bool UseThemeVariantColor
    {
        get => GetValue(UseThemeVariantColorProperty);
        set => SetValue(UseThemeVariantColorProperty, value);
    }

    public Tag()
    {
        _logger.Debug("Tag 控件初始化");
        InitializeComponent();
        AttachedToVisualTree += (_, _) => UpdateTagAppearance();
        ActualThemeVariantChanged += (_, _) => UpdateTagAppearance();
        UpdateTagAppearance();
    }

    private void UpdateTagAppearance()
    {
        if (TagBorder == null || TagText == null)
            return;

        var brush = TagColor;
        if (brush is not ISolidColorBrush solidColorBrush)
        {
            TagBorder.Background = brush;
            TagText.Foreground = new SolidColorBrush(
                TagColorPalette.GetContrastingForeground(ActualThemeVariant));
            return;
        }

        var displayColor = UseThemeVariantColor
            ? TagColorPalette.Resolve(solidColorBrush.Color, ActualThemeVariant)
            : solidColorBrush.Color;

        TagBorder.Background = new SolidColorBrush(displayColor);
        TagText.Foreground = new SolidColorBrush(TagColorPalette.GetContrastingForeground(displayColor));
    }
}
