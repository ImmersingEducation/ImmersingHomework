using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ImmersingHomework.Controls;

public partial class SettingsListItem : UserControl
{
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeAll);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingsListItem, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingsListItem, string?>(nameof(Description));

    public static readonly StyledProperty<object?> LeadingContentProperty =
        AvaloniaProperty.Register<SettingsListItem, object?>(nameof(LeadingContent));

    public static readonly StyledProperty<IBrush?> RowBackgroundProperty =
        AvaloniaProperty.Register<SettingsListItem, IBrush?>(nameof(RowBackground));

    public static readonly StyledProperty<Thickness> RowMarginProperty =
        AvaloniaProperty.Register<SettingsListItem, Thickness>(nameof(RowMargin));

    public static readonly StyledProperty<CornerRadius> RowCornerRadiusProperty =
        AvaloniaProperty.Register<SettingsListItem, CornerRadius>(nameof(RowCornerRadius));

    public static readonly StyledProperty<bool> ShowSeparatorProperty =
        AvaloniaProperty.Register<SettingsListItem, bool>(nameof(ShowSeparator), true);

    public static readonly StyledProperty<bool> IsReorderableProperty =
        AvaloniaProperty.Register<SettingsListItem, bool>(nameof(IsReorderable));

    public event EventHandler? EditRequested;
    public event EventHandler? DeleteRequested;

    internal Control? DragHandle => DragHandleBorder;

    static SettingsListItem()
    {
        TitleProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateTitle());
        DescriptionProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateDescription());
        LeadingContentProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateLeadingContent());
        RowBackgroundProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateRowAppearance());
        RowMarginProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateRowAppearance());
        RowCornerRadiusProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateRowAppearance());
        ShowSeparatorProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateSeparator());
        IsReorderableProperty.Changed.AddClassHandler<SettingsListItem>((item, _) => item.UpdateReorderable());
    }

    public SettingsListItem()
    {
        InitializeComponent();
        DragHandleBorder.Cursor = DragCursor;
        UpdateTitle();
        UpdateDescription();
        UpdateLeadingContent();
        UpdateRowAppearance();
        UpdateSeparator();
        UpdateReorderable();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? LeadingContent
    {
        get => GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public IBrush? RowBackground
    {
        get => GetValue(RowBackgroundProperty);
        set => SetValue(RowBackgroundProperty, value);
    }

    public Thickness RowMargin
    {
        get => GetValue(RowMarginProperty);
        set => SetValue(RowMarginProperty, value);
    }

    public CornerRadius RowCornerRadius
    {
        get => GetValue(RowCornerRadiusProperty);
        set => SetValue(RowCornerRadiusProperty, value);
    }

    public bool ShowSeparator
    {
        get => GetValue(ShowSeparatorProperty);
        set => SetValue(ShowSeparatorProperty, value);
    }

    public bool IsReorderable
    {
        get => GetValue(IsReorderableProperty);
        set => SetValue(IsReorderableProperty, value);
    }

    private void UpdateTitle()
    {
        if (TitleText == null)
            return;

        TitleText.Text = Title ?? string.Empty;
        ToolTip.SetTip(TitleText, string.IsNullOrWhiteSpace(Title) ? null : Title);
        UpdateAutomationNames();
    }

    private void UpdateDescription()
    {
        if (DescriptionText == null)
            return;

        DescriptionText.Text = Description ?? string.Empty;
        DescriptionText.IsVisible = !string.IsNullOrWhiteSpace(Description);
    }

    private void UpdateLeadingContent()
    {
        if (LeadingPresenter == null)
            return;

        LeadingPresenter.Content = LeadingContent;
        LeadingPresenter.IsVisible = LeadingContent != null;
    }

    private void UpdateRowAppearance()
    {
        if (RowBorder == null)
            return;

        RowBorder.Background = RowBackground ?? Brushes.Transparent;
        RowBorder.Margin = RowMargin;
        RowBorder.CornerRadius = RowCornerRadius;
        UpdateContrast();
    }

    private void UpdateSeparator()
    {
        if (SeparatorBorder == null)
            return;

        SeparatorBorder.IsVisible = ShowSeparator;
    }

    private void UpdateReorderable()
    {
        if (DragHandleBorder == null)
            return;

        DragHandleBorder.IsVisible = IsReorderable;
    }

    private void UpdateContrast()
    {
        if (TitleText == null || DescriptionText == null || EditButton == null || DeleteButton == null ||
            DragDots == null)
            return;

        if (RowBackground is SolidColorBrush { Color: var color })
        {
            var isLight = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B > 150;
            var primary = isLight ? Brushes.Black : Brushes.White;
            var secondary = new SolidColorBrush(isLight
                ? Color.FromRgb(64, 64, 64)
                : Color.FromRgb(220, 220, 220));
            var danger = new SolidColorBrush(isLight
                ? Color.FromRgb(164, 38, 38)
                : Color.FromRgb(255, 180, 170));

            TitleText.Foreground = primary;
            DescriptionText.Foreground = secondary;
            EditButton.Foreground = primary;
            DeleteButton.Foreground = danger;
            SetDragDotForeground(secondary);
        }
        else
        {
            TitleText.ClearValue(TextBlock.ForegroundProperty);
            DescriptionText.ClearValue(TextBlock.ForegroundProperty);
            EditButton.ClearValue(Button.ForegroundProperty);
            DeleteButton.ClearValue(Button.ForegroundProperty);
            ClearDragDotForeground();
        }
    }

    private void SetDragDotForeground(IBrush brush)
    {
        foreach (var child in DragDots.Children)
        {
            if (child is Ellipse dot)
                dot.Fill = brush;
        }
    }

    private void ClearDragDotForeground()
    {
        foreach (var child in DragDots.Children)
        {
            if (child is Ellipse dot)
                dot.ClearValue(Shape.FillProperty);
        }
    }

    private void UpdateAutomationNames()
    {
        if (EditButton == null || DeleteButton == null)
            return;

        var title = string.IsNullOrWhiteSpace(Title) ? "条目" : Title;
        AutomationProperties.SetName(EditButton, $"编辑 {title}");
        AutomationProperties.SetName(DeleteButton, $"删除 {title}");
        if (DragHandleBorder != null)
            AutomationProperties.SetName(DragHandleBorder, $"拖动调整 {title} 的顺序");
    }

    private void EditButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
