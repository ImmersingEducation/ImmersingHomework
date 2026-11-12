using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Controls;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class HomeworkItemPanel : UserControl
{
    private static readonly IBrush defaultColor = new SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 240, 255));

    private readonly ILogger _logger = Log.ForContext<HomeworkItemPanel>();
    public static readonly StyledProperty<HomeworkItem> HomeworkItemProperty =
        AvaloniaProperty.Register<HomeworkItemPanel, HomeworkItem>(nameof(HomeworkItem));

    private bool _flag = true;
    private bool _isDragModeEnabled;

    public HomeworkItem HomeworkItem
    {
        get => GetValue(HomeworkItemProperty);
        set => SetValue(HomeworkItemProperty, value);
    }

    /// <summary>
    /// 是否允许拖拽排序。作业卡片是否响应拖拽由
    /// <see cref="HomeworkPanel"/> 统一判断，本属性只负责光标提示。
    /// </summary>
    public bool IsDragModeEnabled
    {
        get => _isDragModeEnabled;
        set
        {
            if (_isDragModeEnabled == value)
                return;

            _isDragModeEnabled = value;
            UpdateCursor();
        }
    }

    public static readonly StyledProperty<bool> IsFrozenProperty =
        AvaloniaProperty.Register<HomeworkItemPanel, bool>(nameof(IsFrozen), false);

    public bool IsFrozen
    {
        get => GetValue(IsFrozenProperty);
        set => SetValue(IsFrozenProperty, value);
    }

    public event Action<HomeworkItem>? EditRequested;

    public HomeworkItemPanel()
    {
        _logger.Debug("HomeworkItemPanel 初始化");
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HomeworkItemProperty)
        {
            _logger.Debug("作业项属性变化");
            UpdatePanel();
        }
        else if (change.Property == IsFrozenProperty)
        {
            UpdateCursor();
        }
    }

    /// <summary>
    /// 请求编辑当前作业。由 <see cref="HomeworkPanel"/> 在确认本次操作是点击
    /// 而不是拖拽后调用。
    /// </summary>
    public void RequestEdit()
    {
        if (IsFrozen || HomeworkItem is null)
            return;

        _logger.Debug("用户点击了作业，请求编辑作业，ID: {Id}", HomeworkItem.Id);
        EditRequested?.Invoke(HomeworkItem);
    }

    private void UpdateCursor()
    {
        CardBorder.Cursor = _isDragModeEnabled && !IsFrozen
            ? new Cursor(StandardCursorType.SizeAll)
            : null;
    }

    private void UpdatePanel()
    {
        _logger.Debug("更新作业项面板");
        if (HomeworkItem == null) 
        {
            _logger.Debug("作业项为空，跳过更新");
            return;
        }
        
        if (!string.IsNullOrEmpty(HomeworkItem.Content))
        {
            ContentText.Text = HomeworkItem.Content;
        }
        TagPanel.Children.Clear();
        
        var tags = HomeworkItem.Tags;
        if (tags == null) return;
        _logger.Debug("作业项有 {Count} 个标签", tags.Count);
        
        var tagColorMap = AppSettings.Instance.Tags
            .Where(t => !string.IsNullOrEmpty(t.Name))
            .ToDictionary(t => t.Name, t => t.Color.ToSolidColorBrush());
        
        for (int i = 0; i < tags.Count; i++)
        {
            var tagModel = tags[i];
            if (!string.IsNullOrEmpty(tagModel.Name))
            {
                tagColorMap.TryGetValue(tagModel.Name, out var tagColor);
                tagColor ??= tagModel.Color.ToSolidColorBrush();
                
                var tag = new Tag
                {
                    TagName = tagModel.Name,
                    TagColor = tagColor
                };
                TagPanel.Children.Add(tag);
            }
        }
        
        _logger.Debug("作业项面板更新完成");
    }
}