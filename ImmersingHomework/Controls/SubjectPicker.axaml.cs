using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class SubjectPicker : UserControl
{
    private readonly ILogger _logger = Log.ForContext<SubjectPicker>();
    private IClassIslandService? _classIslandService;

    public SubjectPicker()
    {
        _logger.Debug("SubjectPicker 初始化");
        InitializeComponent();
    }

    /// <summary>
    /// 注入依赖服务并加载科目列表。本控件在 <c>AddHomeworkWindow.axaml</c> 中以 XAML 方式声明创建，
    /// 构造函数由 XAML 编译器调用，无法传入服务，因此由 <see cref="AddHomeworkWindow"/>
    /// 在 <c>InitializeComponent</c> 之后显式注入。
    /// </summary>
    public void Initialize(IClassIslandService classIslandService)
    {
        _classIslandService = classIslandService;
        LoadSubjects();
    }

    private void LoadSubjects()
    {
        var classIslandService = _classIslandService
                                ?? throw new InvalidOperationException("SubjectPicker 尚未注入 IClassIslandService。");

        List<string> subjects = AppSettings.Instance.EnableClassIslandIPCService.Value &&
                                AppSettings.Instance.ClassIslandTakeoverSubjects.Value
            ? classIslandService.GetSubjects()
            : AppSettings.Instance.Subjects.ToList();

        SubjectPanel.Children.Clear();
        foreach (var subject in subjects)
        {
            SubjectPanel.Children.Add(new RadioButton()
            {
                Content = subject,
                GroupName = "Subjects"
            });
        }
    }

    public string? GetSelectedSubject()
    {
        _logger.Debug("获取选中的科目");
        foreach (var subjectRadioButton in SubjectPanel.Children)
        {
            if (subjectRadioButton is not RadioButton subject) continue;
            if (subject is { IsChecked: true, Content: not null })
            {
                var selectedSubject = subject.Content.ToString();
                _logger.Debug("选中的科目: {Subject}", selectedSubject);
                return selectedSubject;
            }
        }
        _logger.Debug("未选中任何科目");
        return null;
    }

    public void SetSelectedSubject(string? subjectName)
    {
        _logger.Debug("设置选中的科目: {Subject}", subjectName);
        foreach (var subjectRadioButton in SubjectPanel.Children)
        {
            if (subjectRadioButton is not RadioButton subject) continue;
            if (subject.Content?.ToString() == subjectName)
            {
                subject.IsChecked = true;
                _logger.Debug("已选中科目: {Subject}", subjectName);
                break;
            }
        }
    }
}