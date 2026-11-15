using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Controls;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Controls;
using ImmersingHomework.Shared.Models;
using Serilog;
using TagColorModel = ImmersingHomework.Shared.Models.Color;

namespace ImmersingHomework.Views.SettingsPages;

public partial class TagSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<TagSettingsPage>();
    private readonly SettingsListReorder _reorder;
    private static readonly List<Avalonia.Media.Color> PredefinedColors =
    [
        Avalonia.Media.Color.FromRgb(220, 240, 255), // 浅蓝
        Avalonia.Media.Color.FromRgb(255, 240, 220), // 浅橙
        Avalonia.Media.Color.FromRgb(220, 255, 220), // 浅绿
        Avalonia.Media.Color.FromRgb(255, 220, 240), // 浅粉
        Avalonia.Media.Color.FromRgb(240, 240, 220), // 浅黄
        Avalonia.Media.Color.FromRgb(240, 220, 255), // 浅紫
        Avalonia.Media.Color.FromRgb(255, 220, 220), // 浅红
        Avalonia.Media.Color.FromRgb(220, 255, 240)  // 浅青
    ];

    public TagSettingsPage()
    {
        _logger.Debug("TagSettingsPage 初始化");
        InitializeComponent();
        _reorder = new SettingsListReorder(TagPanel, ListScrollViewer, MoveTag);
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("TagSettingsPage 附加到视觉树，刷新标签列表");
            Refresh();
        };
        ActualThemeVariantChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var tags = AppSettings.Instance.Tags;
        _logger.Debug("刷新标签列表，共 {Count} 个标签", tags.Count);

        _reorder.Cancel();
        TagPanel.Children.Clear();
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            var identifierColor = tag.Color.ToAvaloniaColor();
            var displayColor = tag.GetDisplayColor(ActualThemeVariant);
            var row = new SettingsListItem
            {
                Title = tag.Name,
                Description = GetColorName(identifierColor),
                LeadingContent = CreateTagLeadingIcon(displayColor),
                IsReorderable = true,
                RowBackground = new SolidColorBrush(displayColor),
                RowMargin = new Thickness(8, 4),
                RowCornerRadius = new CornerRadius(6),
                ShowSeparator = false
            };
            row.EditRequested += async (_, _) => await EditTag(tag);
            row.DeleteRequested += async (_, _) => await DeleteTag(tag);
            TagPanel.Children.Add(row);
        }

        CountText.Text = $"共 {tags.Count} 个标签";
        EmptyState.IsVisible = tags.Count == 0;
        ListScrollViewer.IsVisible = tags.Count > 0;
    }

    private static Control CreateTagLeadingIcon(Avalonia.Media.Color color)
    {
        return new FASymbolIcon
        {
            Symbol = FASymbol.Tag,
            FontSize = 16,
            Foreground = new SolidColorBrush(TagColorPalette.GetContrastingForeground(color)),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void MoveTag(int oldIndex, int newIndex)
    {
        var tags = AppSettings.Instance.Tags;
        if (oldIndex < 0 || oldIndex >= tags.Count)
            return;

        newIndex = Math.Clamp(newIndex, 0, tags.Count - 1);
        if (oldIndex == newIndex)
            return;

        _logger.Information("调整标签顺序: {OldIndex} -> {NewIndex}", oldIndex, newIndex);
        tags.Move(oldIndex, newIndex);
    }

    private async void AddTagButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击添加标签按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var newTag = await ShowTagDialog(window, null);
        if (newTag == null)
            return;

        if (TagNameExists(newTag.Name, null))
        {
            _logger.Warning("标签名称已存在: {TagName}", newTag.Name);
            return;
        }

        _logger.Information("添加新标签: {TagName}", newTag.Name);
        AppSettings.Instance.Tags.Add(newTag);
        Refresh();
    }

    private async Task EditTag(TagModel tag)
    {
        _logger.Debug("用户编辑标签: {TagName}", tag.Name);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        if (!AppSettings.Instance.Tags.Contains(tag))
        {
            _logger.Warning("待编辑的标签已不存在: {TagName}", tag.Name);
            return;
        }

        var editedTag = await ShowTagDialog(window, tag);
        if (editedTag == null)
            return;

        if (!AppSettings.Instance.Tags.Contains(tag))
        {
            _logger.Warning("编辑完成时标签已不存在: {TagName}", tag.Name);
            return;
        }

        _logger.Information("更新标签: {OldName} -> {NewName}", tag.Name, editedTag.Name);
        // 原地修改以保留作业项中的引用，再立即落盘；自动保存只监听集合变化，不会感知属性变更。
        tag.Name = editedTag.Name;
        tag.Color = editedTag.Color;
        AppSettings.Instance.Save();
        Refresh();
    }

    private async Task DeleteTag(TagModel tag)
    {
        _logger.Information("准备删除标签: {TagName}", tag.Name);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var dialog = new FAContentDialog
        {
            Title = "删除标签",
            Content = $"确定要删除标签「{tag.Name}」吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };
        dialog.UseDangerPrimaryButton();

        var result = await dialog.ShowAsync(window);
        if (result != FAContentDialogResult.Primary)
        {
            _logger.Debug("用户取消删除标签: {TagName}", tag.Name);
            return;
        }

        _logger.Information("用户确认删除标签: {TagName}", tag.Name);
        AppSettings.Instance.Tags.Remove(tag);
        Refresh();
    }

    private async Task<TagModel?> ShowTagDialog(Window window, TagModel? originalTag)
    {
        var isNew = originalTag == null;
        var selectedColor = originalTag?.Color.ToAvaloniaColor() ?? PredefinedColors[0];

        var nameTextBox = new TextBox
        {
            PlaceholderText = "请输入标签名称",
            Text = originalTag?.Name ?? string.Empty,
            Width = 340,
            MaxLength = 50
        };

        var errorText = new TextBlock
        {
            IsVisible = false,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        errorText.Bind(TextBlock.ForegroundProperty,
            new DynamicResourceExtension("SystemFillColorCriticalBrush"));

        var previewTag = new Tag
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var swatchPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8,
            Width = 340
        };

        var swatchItems = new List<(Button Button, Avalonia.Media.Color Color)>();
        var accentBrush = GetThemeBrush("AccentFillColorDefaultBrush", Brushes.DodgerBlue);
        var swatchBorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush", Brushes.Gray);

        void UpdatePreview()
        {
            var name = nameTextBox.Text?.Trim();
            var previewName = string.IsNullOrWhiteSpace(name) ? "标签预览" : name;
            if (previewName.Length > 16)
                previewName = previewName[..16] + "…";
            previewTag.TagName = previewName;
            previewTag.TagColor = new SolidColorBrush(selectedColor);
        }

        void UpdateSwatches()
        {
            foreach (var (button, color) in swatchItems)
            {
                var selected = color == selectedColor;
                button.BorderBrush = selected ? accentBrush : swatchBorderBrush;
                button.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                if (button.Content is TextBlock check)
                    check.IsVisible = selected;
            }
        }

        foreach (var color in PredefinedColors)
        {
            var displayColor = TagColorPalette.Resolve(color, ActualThemeVariant);
            var check = new TextBlock
            {
                Text = "✓",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(TagColorPalette.GetContrastingForeground(displayColor)),
                IsVisible = false
            };
            var swatchButton = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(displayColor),
                BorderBrush = swatchBorderBrush,
                BorderThickness = new Thickness(1),
                Content = check
            };
            ToolTip.SetTip(swatchButton, GetColorName(color));
            AutomationProperties.SetName(swatchButton, $"{GetColorName(color)}标签颜色");

            var swatchColor = color;
            swatchButton.Click += (_, _) =>
            {
                selectedColor = swatchColor;
                UpdateSwatches();
                UpdatePreview();
            };

            swatchItems.Add((swatchButton, color));
            swatchPanel.Children.Add(swatchButton);
        }

        UpdateSwatches();
        UpdatePreview();

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "标签名称" },
                nameTextBox,
                errorText,
                new TextBlock { Text = "标签颜色", Margin = new Thickness(0, 4, 0, 0) },
                swatchPanel,
                new TextBlock { Text = "预览", Margin = new Thickness(0, 4, 0, 0) },
                previewTag
            }
        };

        var dialog = new FAContentDialog
        {
            Title = isNew ? "添加标签" : "编辑标签",
            Content = content,
            PrimaryButtonText = isNew ? "添加" : "保存",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        void Validate()
        {
            var name = nameTextBox.Text?.Trim() ?? string.Empty;
            var duplicate = TagNameExists(name, originalTag);
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name) && !duplicate;
            errorText.IsVisible = !string.IsNullOrWhiteSpace(name) && duplicate;
            errorText.Text = "该标签名称已存在，请输入其他名称。";
        }

        nameTextBox.TextChanged += (_, _) =>
        {
            UpdatePreview();
            Validate();
        };
        dialog.Opened += (_, _) =>
        {
            nameTextBox.Focus();
            if (!isNew)
                nameTextBox.SelectAll();
        };
        Validate();

        var result = await dialog.ShowAsync(window);
        if (result != FAContentDialogResult.Primary)
            return null;

        var tagName = nameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(tagName))
            return null;

        return new TagModel
        {
            Name = tagName,
            Color = TagColorModel.FromAvaloniaColor(selectedColor)
        };
    }

    private static bool TagNameExists(string name, TagModel? exclude)
    {
        foreach (var tag in AppSettings.Instance.Tags)
        {
            if (ReferenceEquals(tag, exclude))
                continue;
            if (tag.Name == name)
                return true;
        }

        return false;
    }

    private static IBrush GetThemeBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush)
            return brush;
        return fallback;
    }

    private static string GetColorName(Avalonia.Media.Color color)
    {
        if (color == Avalonia.Media.Color.FromRgb(220, 240, 255)) return "蓝色";
        if (color == Avalonia.Media.Color.FromRgb(255, 240, 220)) return "橙色";
        if (color == Avalonia.Media.Color.FromRgb(220, 255, 220)) return "绿色";
        if (color == Avalonia.Media.Color.FromRgb(255, 220, 240)) return "粉色";
        if (color == Avalonia.Media.Color.FromRgb(240, 240, 220)) return "黄色";
        if (color == Avalonia.Media.Color.FromRgb(240, 220, 255)) return "紫色";
        if (color == Avalonia.Media.Color.FromRgb(255, 220, 220)) return "红色";
        if (color == Avalonia.Media.Color.FromRgb(220, 255, 240)) return "青色";
        return "自定义";
    }
}
