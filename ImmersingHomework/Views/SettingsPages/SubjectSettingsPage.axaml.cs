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

public partial class SubjectSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<SubjectSettingsPage>();
    private readonly SettingsListReorder _reorder;

    public SubjectSettingsPage()
    {
        _logger.Debug("SubjectSettingsPage 初始化");
        InitializeComponent();
        _reorder = new SettingsListReorder(SubjectPanel, ListScrollViewer, MoveSubject);
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("SubjectSettingsPage 附加到视觉树，刷新科目列表");
            Refresh();
        };
    }

    public void Refresh()
    {
        var subjects = AppSettings.Instance.Subjects;
        _logger.Debug("刷新科目列表，共 {Count} 个科目", subjects.Count);

        _reorder.Cancel();
        SubjectPanel.Children.Clear();
        for (var i = 0; i < subjects.Count; i++)
        {
            var subject = subjects[i];
            var row = new SettingsListItem
            {
                Title = subject,
                LeadingContent = CreateSubjectBadge(subject),
                IsReorderable = true,
                ShowSeparator = i < subjects.Count - 1
            };
            row.EditRequested += async (_, _) => await RenameSubject(subject);
            row.DeleteRequested += async (_, _) => await DeleteSubject(subject);
            SubjectPanel.Children.Add(row);
        }

        CountText.Text = $"共 {subjects.Count} 个科目";
        EmptyState.IsVisible = subjects.Count == 0;
        ListScrollViewer.IsVisible = subjects.Count > 0;
        ClassIslandHint.IsVisible = AppSettings.Instance.EnableClassIslandIPCService.Value
                                    && AppSettings.Instance.ClassIslandTakeoverSubjects.Value;
    }

    private static Control CreateSubjectBadge(string subject)
    {
        var badge = new Border();
        badge.Classes.Add("letterBadge");
        badge.Child = new TextBlock
        {
            Text = subject.Length > 0 ? subject[..1] : "?"
        };
        return badge;
    }

    private void MoveSubject(int oldIndex, int newIndex)
    {
        var subjects = AppSettings.Instance.Subjects;
        if (oldIndex < 0 || oldIndex >= subjects.Count)
            return;

        newIndex = Math.Clamp(newIndex, 0, subjects.Count - 1);
        if (oldIndex == newIndex)
            return;

        _logger.Information("调整科目顺序: {OldIndex} -> {NewIndex}", oldIndex, newIndex);
        subjects.Move(oldIndex, newIndex);
        UpdateSeparators();
    }

    private void UpdateSeparators()
    {
        for (var i = 0; i < SubjectPanel.Children.Count; i++)
        {
            if (SubjectPanel.Children[i] is SettingsListItem row)
                row.ShowSeparator = i < SubjectPanel.Children.Count - 1;
        }
    }

    private async void AddSubjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击添加科目按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var subjectName = await ShowSubjectDialog(window, null);
        if (string.IsNullOrEmpty(subjectName))
            return;

        if (SubjectNameExists(subjectName, null))
        {
            _logger.Warning("科目已存在: {Subject}", subjectName);
            return;
        }

        _logger.Information("添加新科目: {Subject}", subjectName);
        AppSettings.Instance.Subjects.Add(subjectName);
        Refresh();
    }

    private async Task RenameSubject(string oldName)
    {
        _logger.Information("准备重命名科目: {Subject}", oldName);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var newName = await ShowSubjectDialog(window, oldName);
        if (string.IsNullOrEmpty(newName) || newName == oldName)
            return;

        var index = AppSettings.Instance.Subjects.IndexOf(oldName);
        if (index < 0)
        {
            _logger.Warning("待重命名的科目已不存在: {Subject}", oldName);
            return;
        }

        _logger.Information("重命名科目: {OldName} -> {NewName}", oldName, newName);
        AppSettings.Instance.Subjects[index] = newName;
        Refresh();
    }

    private async Task DeleteSubject(string subjectName)
    {
        _logger.Information("准备删除科目: {Subject}", subjectName);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var dialog = new FAContentDialog
        {
            Title = "删除科目",
            Content = $"确定要删除科目「{subjectName}」吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };
        dialog.UseDangerPrimaryButton();

        var result = await dialog.ShowAsync(window);
        if (result != FAContentDialogResult.Primary)
        {
            _logger.Debug("用户取消删除科目: {Subject}", subjectName);
            return;
        }

        _logger.Information("用户确认删除科目: {Subject}", subjectName);
        AppSettings.Instance.Subjects.Remove(subjectName);
        Refresh();
    }

    private async Task<string?> ShowSubjectDialog(Window window, string? oldName)
    {
        var isNew = oldName == null;
        var textBox = new TextBox
        {
            PlaceholderText = "请输入科目名称",
            Text = oldName ?? string.Empty,
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

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "科目名称" },
                textBox,
                errorText
            }
        };

        var dialog = new FAContentDialog
        {
            Title = isNew ? "添加科目" : "编辑科目",
            Content = content,
            PrimaryButtonText = isNew ? "添加" : "保存",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        void Validate()
        {
            var name = textBox.Text?.Trim() ?? string.Empty;
            var duplicate = SubjectNameExists(name, oldName);
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name) && !duplicate;
            errorText.IsVisible = !string.IsNullOrWhiteSpace(name) && duplicate;
            errorText.Text = "该科目已存在，请输入其他名称。";
        }

        textBox.TextChanged += (_, _) => Validate();
        dialog.Opened += (_, _) =>
        {
            textBox.Focus();
            if (!isNew)
                textBox.SelectAll();
        };
        Validate();

        var result = await dialog.ShowAsync(window);
        if (result != FAContentDialogResult.Primary)
            return null;

        return textBox.Text?.Trim();
    }

    private static bool SubjectNameExists(string name, string? exclude)
    {
        foreach (var subject in AppSettings.Instance.Subjects)
        {
            if (subject == name && subject != exclude)
                return true;
        }

        return false;
    }
}
