using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class TagPicker : UserControl
{
    private readonly ILogger _logger = Log.ForContext<TagPicker>();
    private readonly List<PickableTag> _pickableTags = [];
    private readonly Dictionary<string, TagModel> _tagModelMap = [];

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
        _tagModelMap.Clear();

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
            _tagModelMap[tagModel.Name] = tagModel;
        }
        
        _logger.Debug("标签加载完成，共 {Count} 个", _pickableTags.Count);
    }

    public List<TagModel> GetSelectedTags()
    {
        _logger.Debug("获取选中的标签");
        var selectedTags = _pickableTags
            .Where(tag => tag.IsChecked && !string.IsNullOrEmpty(tag.TagName))
            .Select(tag => _tagModelMap.GetValueOrDefault(tag.TagName) ?? new TagModel { Name = tag.TagName! })
            .ToList();
        
        _logger.Debug("选中了 {Count} 个标签", selectedTags.Count);
        return selectedTags;
    }

    public void SetSelectedTags(List<TagModel> selectedTags)
    {
        _logger.Debug("设置选中的标签，共 {Count} 个", selectedTags.Count);
        var selectedNames = new HashSet<string>(selectedTags.Select(t => t.Name));
        foreach (var pickableTag in _pickableTags)
        {
            pickableTag.IsChecked = selectedNames.Contains(pickableTag.TagName);
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