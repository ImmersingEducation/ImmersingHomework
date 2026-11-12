using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class SubjectHomeworkPanel : UserControl
{
    private readonly ILogger _logger = Log.ForContext<SubjectHomeworkPanel>();
    private bool _suppressRefresh;
    private bool _isDragModeEnabled;

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

    public static readonly StyledProperty<bool> IsFrozenProperty =
        AvaloniaProperty.Register<SubjectHomeworkPanel, bool>(nameof(IsFrozen), false);

    public bool IsFrozen
    {
        get => GetValue(IsFrozenProperty);
        set => SetValue(IsFrozenProperty, value);
    }

    /// <summary>
    /// 拖拽模式。开启后科目标题成为可拖拽区域，
    /// 内部的作业卡片也同步进入可拖拽状态。
    /// </summary>
    public bool IsDragModeEnabled
    {
        get => _isDragModeEnabled;
        set
        {
            if (_isDragModeEnabled == value)
                return;

            _isDragModeEnabled = value;
            ApplyDragModeState();
        }
    }

    /// <summary>
    /// 科目标题的拖拽命中区域，由 <see cref="HomeworkPanel"/> 在其上开始科目拖拽。
    /// </summary>
    public Control SubjectDragHandle => SubjectHeader;

    /// <summary>
    /// 科目下的作业卡片容器，用于计算作业卡片的插入位置。
    /// </summary>
    public StackPanel ItemPanelHost => HomeworkItemPanels;

    public event Action<HomeworkItem>? EditRequested;

    public SubjectHomeworkPanel()
    {
        _logger.Debug("SubjectHomeworkPanel 初始化");
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SubjectProperty)
        {
            if (_suppressRefresh) return;
            _logger.Debug("科目属性变化: {Subject}", Subject);
            Refresh();
        }
        else if (change.Property == HomeworkItemsProperty)
        {
            if (_suppressRefresh) return;
            _logger.Debug("作业项列表变化");
            Refresh();
        }
        else if (change.Property == IsFrozenProperty)
        {
            ApplyFrozenState();
        }
    }

    public void SetData(string subject, List<HomeworkItem> homeworkItems)
    {
        _suppressRefresh = true;
        Subject = subject;
        HomeworkItems = homeworkItems;
        _suppressRefresh = false;
        Refresh();
    }

    private void ApplyFrozenState()
    {
        foreach (var child in HomeworkItemPanels.Children)
        {
            if (child is HomeworkItemPanel itemPanel)
            {
                itemPanel.IsFrozen = IsFrozen;
            }
        }

        UpdateHeaderCursor();
    }

    private void ApplyDragModeState()
    {
        UpdateHeaderCursor();

        foreach (var child in HomeworkItemPanels.Children)
        {
            if (child is HomeworkItemPanel itemPanel)
                itemPanel.IsDragModeEnabled = _isDragModeEnabled;
        }
    }

    private void UpdateHeaderCursor()
    {
        SubjectHeader.Cursor = _isDragModeEnabled && !IsFrozen
            ? new Cursor(StandardCursorType.SizeAll)
            : null;
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
                    HomeworkItem = item,
                    IsFrozen = IsFrozen,
                    IsDragModeEnabled = IsDragModeEnabled
                };
                itemPanel.EditRequested += (homeworkItem) => EditRequested?.Invoke(homeworkItem);
                HomeworkItemPanels.Children.Add(itemPanel);
            }
        }
        
        _logger.Debug("科目作业面板刷新完成");
    }
}
