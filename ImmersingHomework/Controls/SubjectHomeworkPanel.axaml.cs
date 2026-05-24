using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class SubjectHomeworkPanel : UserControl
{
    private readonly ILogger _logger = Log.ForContext<SubjectHomeworkPanel>();
    public static readonly StyledProperty<string> SubjectProperty =
        AvaloniaProperty.Register<SubjectHomeworkPanel, string>(nameof(Subject));

    public static readonly StyledProperty<List<HomeworkItem>> HomeworkItemsProperty =
        AvaloniaProperty.Register<SubjectHomeworkPanel, List<HomeworkItem>>(nameof(HomeworkItems), new List<HomeworkItem>());

    public string Subject
    {
        get => GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    public List<HomeworkItem> HomeworkItems
    {
        get => GetValue(HomeworkItemsProperty);
        set => SetValue(HomeworkItemsProperty, value);
    }

    public event Action<HomeworkItem>? EditRequested;

    public SubjectHomeworkPanel()
    {
        _logger.Debug("SubjectHomeworkPanel 初始化");
        InitializeComponent();
        SubjectProperty.Changed.AddClassHandler<SubjectHomeworkPanel>((panel, e) => 
        {
            _logger.Debug("科目属性变化: {Subject}", panel.Subject);
            panel.Refresh();
        });
        HomeworkItemsProperty.Changed.AddClassHandler<SubjectHomeworkPanel>((panel, e) => 
        {
            _logger.Debug("作业项列表变化");
            panel.Refresh();
        });
    }

    public void Refresh()
    {
        _logger.Debug("刷新科目作业面板，科目: {Subject}", Subject);
        if (!string.IsNullOrEmpty(Subject))
        {
            SubjectText.Text = Subject;
        }
        HomeworkItemPanels.Children.Clear();

        var items = HomeworkItems ?? Enumerable.Empty<HomeworkItem>();
        _logger.Debug("科目 {Subject} 有 {Count} 个作业项", Subject, items.Count());
        
        foreach (var item in items)
        {
            if (item != null)
            {
                var itemPanel = new HomeworkItemPanel
                {
                    HomeworkItem = item
                };
                itemPanel.EditRequested += (homeworkItem) => EditRequested?.Invoke(homeworkItem);
                HomeworkItemPanels.Children.Add(itemPanel);
            }
        }
        
        _logger.Debug("科目作业面板刷新完成");
    }
}