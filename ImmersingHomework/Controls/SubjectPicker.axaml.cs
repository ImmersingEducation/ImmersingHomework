using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Services;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class SubjectPicker : UserControl
{
    private readonly ILogger _logger = Log.ForContext<SubjectPicker>();
    public SubjectPicker()
    {
        _logger.Debug("SubjectPicker 初始化");
        InitializeComponent();

        List<string> subjects = AppSettings.Instance.EnableClassIslandIPCService.Value &&
                                AppSettings.Instance.ClassIslandTakeoverSubjects.Value
            ? ClassIslandService.Instance.GetSubjects()
            : AppSettings.Instance.Subjects.ToList();
        
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