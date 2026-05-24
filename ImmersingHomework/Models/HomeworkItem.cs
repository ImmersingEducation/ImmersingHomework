using System;
using System.Collections.Generic;
using Serilog;

namespace ImmersingHomework.Models;

public class HomeworkItem
{
    private readonly ILogger _logger = Log.ForContext<HomeworkItem>();
    public Guid Id { get; init; }
    public string Subject { get; set; }
    public string Content { get; set; }
    public List<string> Tags { get; set; }

    public HomeworkItem(string subject, string content, List<string> tags)
    {
        Id = Guid.NewGuid();
        Subject = subject;
        Content = content;
        Tags = tags;
        _logger.Debug("HomeworkItem 初始化，ID: {Id}", Id);
    }
}