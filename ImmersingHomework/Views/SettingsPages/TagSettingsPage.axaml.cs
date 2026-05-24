using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Controls;
using ImmersingHomework.Models;
using System.Collections.Generic;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class TagSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<TagSettingsPage>();
    private static readonly List<Avalonia.Media.Color> PredefinedColors = new List<Avalonia.Media.Color>
    {
        Avalonia.Media.Color.FromRgb(220, 240, 255), // 浅蓝
        Avalonia.Media.Color.FromRgb(255, 240, 220), // 浅橙
        Avalonia.Media.Color.FromRgb(220, 255, 220), // 浅绿
        Avalonia.Media.Color.FromRgb(255, 220, 240), // 浅粉
        Avalonia.Media.Color.FromRgb(240, 240, 220), // 浅黄
        Avalonia.Media.Color.FromRgb(240, 220, 255), // 浅紫
        Avalonia.Media.Color.FromRgb(255, 220, 220), // 浅红
        Avalonia.Media.Color.FromRgb(220, 255, 240)  // 浅青
    };

    public TagSettingsPage()
    {
        _logger.Debug("TagSettingsPage 初始化");
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) => 
        {
            _logger.Debug("TagSettingsPage 附加到视觉树，刷新标签列表");
            Refresh();
        };
    }

    public void Refresh()
    {
        _logger.Debug("刷新标签列表，共 {Count} 个标签", AppSettings.Instance.Tags.Count);
        TagPanel.Children.Clear();
        foreach (var tag in AppSettings.Instance.Tags)
        {
            var tagControl = new Tag { TagName = tag.Name, TagColor = tag.Color.ToSolidColorBrush() };
            var button = new Button
            {
                Content = tagControl,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            button.Click += async (s, e) => await OnTagButtonClick(tag);
            TagPanel.Children.Add(button);
        }
    }

    private async void AddTagButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击添加标签按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var newTag = new TagModel
        {
            Name = "",
            Color = Models.Color.FromAvaloniaColor(PredefinedColors[0])
        };

        var (result, deleted) = await ShowTagEditDialog(window, newTag, true);

        if (result == FAContentDialogResult.Primary && !string.IsNullOrEmpty(newTag.Name))
        {
            if (IsTagNameExists(newTag.Name))
            {
                _logger.Warning("标签名称已存在: {TagName}", newTag.Name);
                var errorDialog = new FAContentDialog()
                {
                    Title = "错误",
                    Content = "该标签名称已存在，请输入其他名称。",
                    CloseButtonText = "确定"
                };
                await errorDialog.ShowAsync(window);
                return;
            }

            _logger.Information("添加新标签: {TagName}", newTag.Name);
            AppSettings.Instance.Tags.Add(newTag);
            Refresh();
        }
    }

    private async System.Threading.Tasks.Task OnTagButtonClick(TagModel tag)
    {
        _logger.Debug("用户点击标签: {TagName}", tag.Name);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var editTag = new TagModel
        {
            Name = tag.Name,
            Color = tag.Color
        };

        var (result, deleted) = await ShowTagEditDialog(window, editTag, false);

        if (deleted)
        {
            _logger.Information("准备删除标签: {TagName}", tag.Name);
            var confirmDialog = new FAContentDialog()
            {
                Title = "删除标签",
                Content = $"确定要删除标签 \"{tag.Name}\" 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消"
            };

            var confirmResult = await confirmDialog.ShowAsync(window);

            if (confirmResult == FAContentDialogResult.Primary)
            {
                _logger.Information("确认删除标签: {TagName}", tag.Name);
                AppSettings.Instance.Tags.Remove(tag);
                Refresh();
            }
        }
        else if (result == FAContentDialogResult.Primary)
        {
            if (!string.Equals(tag.Name, editTag.Name) && IsTagNameExists(editTag.Name))
            {
                _logger.Warning("标签名称已存在: {TagName}", editTag.Name);
                var errorDialog = new FAContentDialog()
                {
                    Title = "错误",
                    Content = "该标签名称已存在，请输入其他名称。",
                    CloseButtonText = "确定"
                };
                await errorDialog.ShowAsync(window);
                return;
            }

            _logger.Information("更新标签: {OldName} -> {NewName}", tag.Name, editTag.Name);
            tag.Name = editTag.Name;
            tag.Color = editTag.Color;
            Refresh();
        }
    }

    private async System.Threading.Tasks.Task<(FAContentDialogResult, bool)> ShowTagEditDialog(Window window, TagModel tag, bool isNew)
    {
        _logger.Debug("显示标签编辑对话框，新建: {IsNew}", isNew);
        var nameTextBox = new TextBox
        {
            PlaceholderText = "请输入标签名称",
            Text = tag.Name,
            Width = 300
        };

        var colorComboBox = new ComboBox
        {
            Width = 300
        };

        foreach (var color in PredefinedColors)
        {
            var colorPreview = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(color)
            };
            var colorItem = new ComboBoxItem
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        colorPreview,
                        new TextBlock { Text = GetColorName(color) }
                    }
                },
                Tag = color
            };
            colorComboBox.Items.Add(colorItem);
            if (tag.Color.ToAvaloniaColor() == color)
            {
                colorComboBox.SelectedItem = colorItem;
            }
        }

        if (colorComboBox.SelectedItem == null && colorComboBox.Items.Count > 0)
        {
            colorComboBox.SelectedItem = colorComboBox.Items[0];
        }

        var contentPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 16
        };

        var nameLabel = new TextBlock { Text = "标签名称" };
        var colorLabel = new TextBlock { Text = "标签颜色" };
        contentPanel.Children.Add(nameLabel);
        contentPanel.Children.Add(nameTextBox);
        contentPanel.Children.Add(colorLabel);
        contentPanel.Children.Add(colorComboBox);

        var dialog = new FAContentDialog
        {
            Title = isNew ? "添加标签" : "编辑标签",
            Content = contentPanel,
            PrimaryButtonText = isNew ? "添加" : "保存",
            SecondaryButtonText = isNew ? null : "删除",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(window);
        var deleted = false;

        if (result == FAContentDialogResult.Secondary)
        {
            deleted = true;
        }
        else if (result == FAContentDialogResult.Primary)
        {
            tag.Name = nameTextBox.Text?.Trim() ?? "";
            if (colorComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is Avalonia.Media.Color selectedColor)
            {
                tag.Color = Models.Color.FromAvaloniaColor(selectedColor);
            }
        }

        return (result, deleted);
    }

    private bool IsTagNameExists(string name)
    {
        foreach (var tag in AppSettings.Instance.Tags)
        {
            if (tag.Name == name)
                return true;
        }
        return false;
    }

    private string GetColorName(Avalonia.Media.Color color)
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
