using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Controls;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class HomeworkTemplateSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkTemplateSettingsPage>();
    private readonly SettingsListReorder _reorder;

    public HomeworkTemplateSettingsPage()
    {
        _logger.Debug("HomeworkTemplateSettingsPage 初始化");
        InitializeComponent();
        _reorder = new SettingsListReorder(HomeworkTemplatePanel, ListScrollViewer, MoveTemplate);
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("HomeworkTemplateSettingsPage 附加到视觉树，刷新模板列表");
            Refresh();
        };
    }

    public void Refresh()
    {
        var templates = AppSettings.Instance.HomeworkTemplates;
        _logger.Debug("刷新作业模板列表，共 {Count} 个模板", templates.Count);

        _reorder.Cancel();
        HomeworkTemplatePanel.Children.Clear();
        for (var i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            var parameterCount = CountParameters(template);
            var row = new SettingsListItem
            {
                Title = template,
                Description = parameterCount == 0 ? "无参数" : $"{parameterCount} 个参数",
                LeadingContent = CreateTemplateBadge(),
                IsReorderable = true,
                ShowSeparator = i < templates.Count - 1
            };
            row.EditRequested += async (_, _) => await EditTemplate(template);
            row.DeleteRequested += async (_, _) => await DeleteTemplate(template);
            HomeworkTemplatePanel.Children.Add(row);
        }

        CountText.Text = $"共 {templates.Count} 个模板";
        EmptyState.IsVisible = templates.Count == 0;
        ListScrollViewer.IsVisible = templates.Count > 0;
    }

    private static Control CreateTemplateBadge()
    {
        var badge = new Border();
        badge.Classes.Add("codeBadge");
        badge.Child = new TextBlock { Text = "[]" };
        return badge;
    }

    private void MoveTemplate(int oldIndex, int newIndex)
    {
        var templates = AppSettings.Instance.HomeworkTemplates;
        if (oldIndex < 0 || oldIndex >= templates.Count)
            return;

        newIndex = Math.Clamp(newIndex, 0, templates.Count - 1);
        if (oldIndex == newIndex)
            return;

        _logger.Information("调整作业模板顺序: {OldIndex} -> {NewIndex}", oldIndex, newIndex);
        templates.Move(oldIndex, newIndex);
        UpdateSeparators();
    }

    private void UpdateSeparators()
    {
        for (var i = 0; i < HomeworkTemplatePanel.Children.Count; i++)
        {
            if (HomeworkTemplatePanel.Children[i] is SettingsListItem row)
                row.ShowSeparator = i < HomeworkTemplatePanel.Children.Count - 1;
        }
    }

    private async void AddTemplateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击添加作业模板按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var templateName = await ShowTemplateDialog(window, null);
        if (string.IsNullOrEmpty(templateName))
            return;

        if (TemplateNameExists(templateName, null))
        {
            _logger.Warning("作业模板已存在: {Template}", templateName);
            return;
        }

        _logger.Information("添加新作业模板: {Template}", templateName);
        AppSettings.Instance.HomeworkTemplates.Add(templateName);
        Refresh();
    }

    private async Task EditTemplate(string oldName)
    {
        _logger.Information("准备编辑作业模板: {Template}", oldName);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var newName = await ShowTemplateDialog(window, oldName);
        if (string.IsNullOrEmpty(newName) || newName == oldName)
            return;

        var index = AppSettings.Instance.HomeworkTemplates.IndexOf(oldName);
        if (index < 0)
        {
            _logger.Warning("待编辑的作业模板已不存在: {Template}", oldName);
            return;
        }

        _logger.Information("更新作业模板: {OldName} -> {NewName}", oldName, newName);
        AppSettings.Instance.HomeworkTemplates[index] = newName;
        Refresh();
    }

    private async Task DeleteTemplate(string templateName)
    {
        _logger.Information("准备删除作业模板: {Template}", templateName);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var dialog = new FAContentDialog
        {
            Title = "删除作业模板",
            Content = $"确定要删除作业模板「{templateName}」吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };
        dialog.UseDangerPrimaryButton();

        var result = await dialog.ShowAsync(window);
        if (result != FAContentDialogResult.Primary)
        {
            _logger.Debug("用户取消删除作业模板: {Template}", templateName);
            return;
        }

        _logger.Information("用户确认删除作业模板: {Template}", templateName);
        AppSettings.Instance.HomeworkTemplates.Remove(templateName);
        Refresh();
    }

    private async Task<string?> ShowTemplateDialog(Window window, string? oldName)
    {
        var isNew = oldName == null;
        var textBox = new TextBox
        {
            PlaceholderText = "请输入作业模板名称",
            Text = oldName ?? string.Empty,
            Width = 340,
            MaxLength = 100
        };

        var hintText = new TextBlock
        {
            Text = "一对中括号 [] 代表一个参数。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        hintText.Bind(TextBlock.ForegroundProperty,
            new DynamicResourceExtension("TextFillColorSecondaryBrush"));

        var countText = new TextBlock
        {
            Text = "参数数量：0",
            FontSize = 12
        };
        countText.Bind(TextBlock.ForegroundProperty,
            new DynamicResourceExtension("TextFillColorSecondaryBrush"));

        var errorText = new TextBlock
        {
            IsVisible = false,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        errorText.Bind(TextBlock.ForegroundProperty,
            new DynamicResourceExtension("SystemFillColorCriticalBrush"));

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "作业模板名称" },
                textBox,
                hintText,
                countText,
                errorText
            }
        };

        var dialog = new FAContentDialog
        {
            Title = isNew ? "添加作业模板" : "编辑作业模板",
            Content = content,
            PrimaryButtonText = isNew ? "添加" : "保存",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        void UpdateCount()
        {
            countText.Text = $"参数数量：{CountParameters(textBox.Text)}";
        }

        void Validate()
        {
            var name = textBox.Text?.Trim() ?? string.Empty;
            var duplicate = TemplateNameExists(name, oldName);
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name) && !duplicate;
            errorText.IsVisible = !string.IsNullOrWhiteSpace(name) && duplicate;
            errorText.Text = "该作业模板已存在，请输入其他名称。";
        }

        textBox.TextChanged += (_, _) =>
        {
            UpdateCount();
            Validate();
        };
        dialog.Opened += (_, _) =>
        {
            textBox.Focus();
            if (!isNew)
                textBox.SelectAll();
        };
        UpdateCount();
        Validate();

        var result = await dialog.ShowAsync(window);
        if (result != FAContentDialogResult.Primary)
            return null;

        return textBox.Text?.Trim();
    }

    private static bool TemplateNameExists(string name, string? exclude)
    {
        foreach (var template in AppSettings.Instance.HomeworkTemplates)
        {
            if (template == name && template != exclude)
                return true;
        }

        return false;
    }

    private static int CountParameters(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf("[]", index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += 2;
        }

        return count;
    }
}
