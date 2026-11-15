using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Serilog;
using TagColorPalette = ImmersingHomework.Shared.Models.TagColorPalette;

namespace ImmersingHomework.Controls;

public partial class PickableTag : UserControl
{
    private readonly ILogger _logger = Log.ForContext<PickableTag>();
    public static readonly StyledProperty<string> TagNameProperty =
        AvaloniaProperty.Register<PickableTag, string>(nameof(TagName));

    public static readonly StyledProperty<IBrush> TagColorProperty =
        AvaloniaProperty.Register<PickableTag, IBrush>(nameof(TagColor), new SolidColorBrush(Color.FromRgb(220, 240, 255)));

    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<PickableTag, bool>(nameof(IsChecked));

    public string TagName
    {
        get => GetValue(TagNameProperty);
        set => SetValue(TagNameProperty, value);
    }

    public IBrush TagColor
    {
        get => GetValue(TagColorProperty);
        set => SetValue(TagColorProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public PickableTag()
    {
        _logger.Debug("PickableTag 初始化");
        InitializeComponent();

        IsCheckedProperty.Changed.AddClassHandler<PickableTag>((tag, e) => 
        {
            _logger.Debug("选中状态变化: {IsChecked}", tag.IsChecked);
            tag.UpdateToggleButtonState();
            tag.UpdateVisual();
        });
        TagNameProperty.Changed.AddClassHandler<PickableTag>((tag, e) => 
        {
            _logger.Debug("标签名称变化: {TagName}", tag.TagName);
            tag.UpdateTagName();
        });
        TagColorProperty.Changed.AddClassHandler<PickableTag>((tag, e) => 
        {
            tag.UpdateVisual();
        });

        TagToggleButton.IsCheckedChanged += (sender, args) =>
        {
            IsChecked = TagToggleButton.IsChecked ?? false;
        };

        this.AttachedToVisualTree += (sender, args) =>
        {
            _logger.Debug("PickableTag 已附加到视觉树，名称: {TagName}", TagName);
            UpdateTagName();
            UpdateVisual();
        };
        ActualThemeVariantChanged += (_, _) => UpdateVisual();
    }

    private void UpdateTagName()
    {
        if (TagText != null && !string.IsNullOrEmpty(TagName))
        {
            TagText.Text = TagName;
        }
    }

    /// <summary>
    /// 将 <see cref="IsChecked"/> 同步到底层 ToggleButton，保证程序化设置选中状态时视觉表现一致。
    /// 值相同时不会触发变更事件，因此不会与 ToggleButton.IsCheckedChanged 形成循环。
    /// </summary>
    private void UpdateToggleButtonState()
    {
        if (TagToggleButton != null && TagToggleButton.IsChecked != IsChecked)
        {
            TagToggleButton.IsChecked = IsChecked;
        }
    }

    private void UpdateVisual()
    {
        _logger.Debug("更新视觉状态，选中: {IsChecked}", IsChecked);
        if (TagToggleButton == null || TagText == null) return;

        var displayBrush = GetDisplayBrush();
        var background = IsChecked ? LightenBrush(displayBrush, 0.3f) : displayBrush;

        TagToggleButton.Background = background;
        TagText.Foreground = new SolidColorBrush(background is ISolidColorBrush { Color: var color }
            ? TagColorPalette.GetContrastingForeground(color)
            : TagColorPalette.GetContrastingForeground(ActualThemeVariant));
    }

    private IBrush GetDisplayBrush()
    {
        return TagColor is ISolidColorBrush { Color: var color }
            ? new SolidColorBrush(TagColorPalette.Resolve(color, ActualThemeVariant))
            : TagColor;
    }

    private IBrush LightenBrush(IBrush brush, float amount)
    {
        if (brush is SolidColorBrush solidColorBrush)
        {
            var color = solidColorBrush.Color;
            var r = (byte)Math.Min(255, color.R + (255 - color.R) * amount);
            var g = (byte)Math.Min(255, color.G + (255 - color.G) * amount);
            var b = (byte)Math.Min(255, color.B + (255 - color.B) * amount);
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }
        return brush;
    }
}