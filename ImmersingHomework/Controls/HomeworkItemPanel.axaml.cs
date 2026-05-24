using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class HomeworkItemPanel : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkItemPanel>();
    public static readonly StyledProperty<HomeworkItem> HomeworkItemProperty =
        AvaloniaProperty.Register<HomeworkItemPanel, HomeworkItem>(nameof(HomeworkItem));

    public HomeworkItem HomeworkItem
    {
        get => GetValue(HomeworkItemProperty);
        set => SetValue(HomeworkItemProperty, value);
    }

    public event Action<HomeworkItem>? EditRequested;

    public HomeworkItemPanel()
    {
        _logger.Debug("HomeworkItemPanel 初始化");
        InitializeComponent();
        HomeworkItemProperty.Changed.AddClassHandler<HomeworkItemPanel>((panel, e) => 
        {
            _logger.Debug("作业项属性变化");
            panel.UpdatePanel();
        });
    }

    private void MoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Debug("用户点击了更多按钮，请求编辑作业，ID: {Id}", HomeworkItem?.Id);
        EditRequested?.Invoke(HomeworkItem);
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
        
        var defaultColors = new List<IBrush>
        {
            new SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 240, 255)), 
            new SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 255, 230)), 
            new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 250, 220)), 
            new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 230, 255)), 
            new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 235, 230))
        };

        var tags = HomeworkItem.Tags ?? Enumerable.Empty<string>();
        _logger.Debug("作业项有 {Count} 个标签", tags.Count());
        
        for (int i = 0; i < tags.Count(); i++)
        {
            var tagName = tags.ElementAt(i);
            if (!string.IsNullOrEmpty(tagName))
            {
                IBrush tagColor = defaultColors[i % defaultColors.Count];
                
                // 在 AppSettings 中查找对应标签的颜色
                var tagModel = AppSettings.Instance.Tags.FirstOrDefault(t => t.Name == tagName);
                if (tagModel != null)
                {
                    tagColor = tagModel.Color.ToSolidColorBrush();
                }
                
                var tag = new Tag
                {
                    TagName = tagName,
                    TagColor = tagColor
                };
                TagPanel.Children.Add(tag);
            }
        }
        
        _logger.Debug("作业项面板更新完成");
    }
}