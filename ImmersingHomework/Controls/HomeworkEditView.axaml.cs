using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class HomeworkEditView : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkEditView>();
    private IClassIslandService? _classIslandService;
    private IHomeworkStorageService? _storageService;
    private HomeworkItem? _editingItem;
    private DateOnly _date;

    /// <summary>
    /// 保存成功后触发，参数为保存后的作业项以及是否为新增。
    /// </summary>
    public event Action<HomeworkItem, bool>? Saved;

    /// <summary>
    /// 删除成功后触发。
    /// </summary>
    public event Action<HomeworkItem>? Deleted;

    public HomeworkEditView()
    {
        InitializeComponent();
    }

    private IHomeworkStorageService StorageService =>
        _storageService ?? throw new InvalidOperationException("HomeworkEditView 尚未初始化。");

    /// <summary>
    /// 注入依赖服务。本控件在 MainWindow.axaml 中以 XAML 方式创建，无法通过构造函数注入。
    /// </summary>
    public void Initialize(IClassIslandService classIslandService, IHomeworkStorageService storageService)
    {
        _classIslandService = classIslandService;
        _storageService = storageService;
        BuildSubjectComboBox();
    }

    /// <summary>
    /// 载入要编辑的作业；<paramref name="item"/> 为 <see langword="null"/> 时表示新增作业。
    /// </summary>
    public void LoadHomework(HomeworkItem? item, DateOnly date)
    {
        _editingItem = item;
        _date = date;

        HideValidation();
        DeleteButton.IsVisible = item is not null;
        BuildSubjectComboBox();

        if (item is null)
        {
            SubjectComboBox.SelectedItem = null;
            ContentInput.SetHomeworkContent(string.Empty, null, null);
            TagPicker.ClearSelection();
            return;
        }

        SelectSubject(item.Subject);
        ContentInput.SetHomeworkContent(item.Content, item.TemplateName, item.TemplateParameters);
        TagPicker.SetSelectedTags(item.Tags ?? []);
    }

    public void BuildSubjectComboBox()
    {
        var useClassIslandSubjects = AppSettings.Instance.EnableClassIslandIPCService.Value
                                     && AppSettings.Instance.ClassIslandTakeoverSubjects.Value;

        List<string> subjects;
        if (useClassIslandSubjects && _classIslandService is not null)
        {
            try
            {
                subjects = _classIslandService.GetSubjects();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "获取 ClassIsland 科目失败，改用本地科目列表");
                subjects = AppSettings.Instance.Subjects.ToList();
            }
        }
        else
        {
            subjects = AppSettings.Instance.Subjects.ToList();
        }

        var currentSubject = GetSelectedSubject();
        SubjectComboBox.Items.Clear();

        foreach (var subject in subjects.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
            SubjectComboBox.Items.Add(new FAComboBoxItem { Content = subject });

        if (!string.IsNullOrWhiteSpace(currentSubject))
            SelectSubject(currentSubject);
    }

    /// <summary>
    /// 抽屉退出时调用。科目和内容都存在时自动保存；字段不完整时直接放弃，允许关闭。
    /// 返回 <see langword="false"/> 表示保存失败，此时应保持抽屉打开。
    /// </summary>
    public bool TrySaveIfReady()
    {
        HideValidation();

        var subject = GetSelectedSubject()?.Trim();
        var content = ContentInput.GetContent().Trim();
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(content))
            return true;

        var isNew = _editingItem is null;
        var item = CreateHomeworkItem(subject, content);

        try
        {
            var homework = StorageService.Load(_date) ?? new Homework(_date, []);
            if (!isNew)
                homework.RemoveHomeworkItem(_editingItem!);

            homework.AddHomeworkItem(item);
            StorageService.Save(homework);

            _editingItem = item;
            _logger.Information("作业已自动保存，ID: {Id}, 新增: {IsNew}", item.Id, isNew);
            Saved?.Invoke(item, isNew);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "自动保存作业失败");
            ShowValidation("保存失败，请查看日志。");
            return false;
        }
    }

    private HomeworkItem CreateHomeworkItem(string subject, string content)
    {
        var tags = TagPicker.GetSelectedTags();
        var templateName = ContentInput.GetTemplateName();
        var templateParameters = ContentInput.GetTemplateParameters();

        if (_editingItem is null)
        {
            return new HomeworkItem(subject, content, tags)
            {
                TemplateName = templateName,
                TemplateParameters = templateParameters
            };
        }

        // HomeworkItem.Id 是 init-only，编辑时需要重新构造对象并保留原 ID。
        return new HomeworkItem(subject, content, tags)
        {
            Id = _editingItem.Id,
            TemplateName = templateName,
            TemplateParameters = templateParameters
        };
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_editingItem is null)
            return;

        _ = DeleteHomeworkAsync(_editingItem);
    }

    private async Task DeleteHomeworkAsync(HomeworkItem item)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
            return;

        var dialog = new FAContentDialog
        {
            Title = "删除作业",
            Content = $"确定要删除 {item.Subject} 的这条作业吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };

        if (await dialog.ShowAsync(window) != FAContentDialogResult.Primary)
            return;

        try
        {
            var homework = StorageService.Load(_date);
            if (homework is not null)
            {
                homework.RemoveHomeworkItem(item);
                StorageService.Save(homework);
            }

            _editingItem = null;
            _logger.Information("作业已删除，ID: {Id}", item.Id);
            Deleted?.Invoke(item);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "删除作业失败");
            ShowValidation("删除失败，请查看日志。");
        }
    }

    private void SubjectComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        HideValidation();

    private string? GetSelectedSubject() =>
        SubjectComboBox.SelectedItem is FAComboBoxItem item
            ? item.Content?.ToString()
            : SubjectComboBox.SelectedItem?.ToString();

    private void SelectSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            SubjectComboBox.SelectedItem = null;
            return;
        }

        EnsureSubjectItem(subject);

        foreach (var item in SubjectComboBox.Items)
        {
            if (item is FAComboBoxItem comboItem &&
                string.Equals(comboItem.Content?.ToString(), subject, StringComparison.Ordinal))
            {
                SubjectComboBox.SelectedItem = comboItem;
                return;
            }
        }
    }

    private void EnsureSubjectItem(string subject)
    {
        foreach (var item in SubjectComboBox.Items)
        {
            if (item is FAComboBoxItem comboItem &&
                string.Equals(comboItem.Content?.ToString(), subject, StringComparison.Ordinal))
                return;
        }

        SubjectComboBox.Items.Add(new FAComboBoxItem { Content = subject });
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.IsVisible = true;
    }

    private void HideValidation()
    {
        ValidationText.Text = string.Empty;
        ValidationText.IsVisible = false;
    }
}
