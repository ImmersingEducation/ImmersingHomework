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

    /// <summary>
    /// 已选标签名集合。由于 <see cref="SetSelectedTags"/> 可能在标签控件加载（附加到视觉树）之前被调用，
    /// 选中状态需先记录在此，并在 <see cref="LoadTags"/> 重建控件时重新应用，避免选中状态丢失。
    /// </summary>
    private readonly HashSet<string> _selectedTagNames = [];

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
                TagColor = tagModel.Color.ToSolidColorBrush(),
                IsChecked = _selectedTagNames.Contains(tagModel.Name)
            };
            PickableTagPanel.Children.Add(pickableTag);
            _pickableTags.Add(pickableTag);
            _tagModelMap[tagModel.Name] = tagModel;
        }
        
        _logger.Debug("标签加载完成，共 {Count} 个，已恢复选中 {SelectedCount} 个", _pickableTags.Count, _selectedTagNames.Count);
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
        _selectedTagNames.Clear();
        foreach (var tag in selectedTags)
        {
            if (!string.IsNullOrEmpty(tag.Name)) _selectedTagNames.Add(tag.Name);
        }

        // 标签控件尚未加载（如编辑对话框附加到视觉树早于本控件）时，
        // 选中状态记录在 _selectedTagNames 中，待 LoadTags 时应用。
        foreach (var pickableTag in _pickableTags)
        {
            pickableTag.IsChecked = _selectedTagNames.Contains(pickableTag.TagName);
        }
    }

    public void ClearSelection()
    {
        _logger.Debug("清除所有标签选择");
        _selectedTagNames.Clear();
        foreach (var pickableTag in _pickableTags)
        {
            pickableTag.IsChecked = false;
        }
    }
}