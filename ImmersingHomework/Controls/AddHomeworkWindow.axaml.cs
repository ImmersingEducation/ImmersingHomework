using System;
using System.Collections.Generic;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class AddHomeworkWindow : UserControl
{
    private readonly ILogger _logger = Log.ForContext<AddHomeworkWindow>();
    public HomeworkItem? Result { get; private set; }
    public bool IsDeleted { get; private set; }
    private readonly HomeworkItem? _existingItem;
    private FAContentDialog? _dialog;

    public string Title { get; private set; }
    public string? SecondaryButtonText { get; private set; }

    public AddHomeworkWindow()
    {
        _logger.Information("AddHomeworkWindow 初始化（添加新作业）");
        InitializeComponent();
        Title = "添加作业项";
        SecondaryButtonText = null;
    }

    public AddHomeworkWindow(HomeworkItem existingItem) : this()
    {
        _logger.Information("AddHomeworkWindow 初始化（编辑现有作业），ID: {Id}", existingItem.Id);
        _existingItem = existingItem;
        Title = "修改作业项";
        SecondaryButtonText = "删除";
        
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("填充编辑表单");
            SubjectPicker.SetSelectedSubject(existingItem.Subject);
            ContentInput.Text = existingItem.Content;
            TagPicker.SetSelectedTags(existingItem.Tags);
        };
    }

    public void SetDialog(FAContentDialog dialog)
    {
        _logger.Debug("设置对话框引用");
        _dialog = dialog;
    }

    public void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        _logger.Debug("用户点击了确定按钮");
        Result = GetHomework();
        if (Result != null)
        {
            if (_existingItem != null)
            {
                _logger.Debug("更新现有作业，ID: {Id}", _existingItem.Id);
                Result = new HomeworkItem(Result.Subject, Result.Content, Result.Tags)
                {
                    Id = _existingItem.Id
                };
            }
            IsDeleted = false;
            _logger.Debug("作业已准备好保存");
        }
        else
        {
            _logger.Debug("作业验证失败，取消操作");
            args.Cancel = true;
        }
    }

    public void OnSecondaryButtonClick()
    {
        _logger.Information("用户点击了删除按钮");
        Result = null;
        IsDeleted = true;
    }

    public void OnCloseButtonClick()
    {
        _logger.Debug("用户关闭了对话框");
        Result = null;
        IsDeleted = false;
    }

    public HomeworkItem? GetHomework()
    {
        _logger.Debug("获取作业数据");
        string? subject = SubjectPicker.GetSelectedSubject();
        string? content = ContentInput?.Text?.Trim();
        List<string> tags = TagPicker.GetSelectedTags();

        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(content))
        {
            _logger.Debug("作业验证失败：科目或内容为空");
            return null;
        }
        
        _logger.Debug("作业数据获取成功，科目: {Subject}, 标签: {TagCount}", subject, tags.Count);
        return new HomeworkItem(subject, content, tags);
    }
}
