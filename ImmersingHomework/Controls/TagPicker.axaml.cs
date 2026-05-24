using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class TagPicker : UserControl
{
    private readonly ILogger _logger = Log.ForContext<TagPicker>();
    private readonly List<PickableTag> _pickableTags = [];

    public TagPicker()
    {
        _logger.Debug("TagPicker 初始化");
        InitializeComponent();

        this.AttachedToVisualTree += (sender, args) =>
        {
            _logger.Debug("TagPicker 已附加到视觉树，开始加载标签");
            LoadTags();
        };
    }

    private void LoadTags()
    {
        _logger.Debug("加载标签列表");
        if (PickableTagPanel == null) return;

        PickableTagPanel.Children.Clear();
        _pickableTags.Clear();

        _logger.Debug("从设置中加载 {Count} 个标签", AppSettings.Instance.Tags.Count);
        foreach (var tagModel in AppSettings.Instance.Tags)
        {
            var pickableTag = new PickableTag
            {
                TagName = tagModel.Name,
                TagColor = tagModel.Color.ToSolidColorBrush()
            };
            PickableTagPanel.Children.Add(pickableTag);
            _pickableTags.Add(pickableTag);
        }
        
        _logger.Debug("标签加载完成，共 {Count} 个", _pickableTags.Count);
    }

    public List<string> GetSelectedTags()
    {
        _logger.Debug("获取选中的标签");
        var selectedTags = _pickableTags
            .Where(tag => tag.IsChecked)
            .Select(tag => tag.TagName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
        
        _logger.Debug("选中了 {Count} 个标签", selectedTags.Count);
        return selectedTags;
    }

    public void SetSelectedTags(List<string> selectedTags)
    {
        _logger.Debug("设置选中的标签，共 {Count} 个", selectedTags.Count);
        foreach (var pickableTag in _pickableTags)
        {
            pickableTag.IsChecked = selectedTags.Contains(pickableTag.TagName);
        }
    }

    public void ClearSelection()
    {
        _logger.Debug("清除所有标签选择");
        foreach (var pickableTag in _pickableTags)
        {
            pickableTag.IsChecked = false;
        }
    }
}