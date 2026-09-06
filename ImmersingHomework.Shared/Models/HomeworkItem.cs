using System;
using System.Collections.Generic;
using Serilog;

namespace ImmersingHomework.Shared.Models;

public class HomeworkItem
{
    private readonly ILogger _logger = Log.ForContext<HomeworkItem>();
    public Guid Id { get; init; }
    public string Subject { get; set; }
    public string Content { get; set; }
    public List<TagModel> Tags { get; set; }
    public string? TemplateName { get; set; }
    public List<string>? TemplateParameters { get; set; }

    public HomeworkItem(string subject, string content, List<TagModel> tags)
    {
        Id = Guid.NewGuid();
        Subject = subject;
        Content = content;
        Tags = tags;
        _logger.Debug("HomeworkItem 初始化，ID: {Id}", Id);
    }
}
