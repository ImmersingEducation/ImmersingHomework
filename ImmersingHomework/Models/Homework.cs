using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ImmersingHomework.Models;

public class Homework
{
    private readonly ILogger _logger = Log.ForContext<Homework>();
    public DateOnly Date { get; init; }
    public List<HomeworkItem> HomeworkItems { get; set; }

    public Homework(DateOnly date, List<HomeworkItem> homeworkItems)
    {
        _logger.Debug("Homework 初始化，日期: {Date}", date);
        Date = date;
        HomeworkItems = homeworkItems;
    }

    public void AddHomeworkItem(HomeworkItem homeworkItem)
    {
        _logger.Information("添加作业项，科目: {Subject}", homeworkItem.Subject);
        HomeworkItems.Add(homeworkItem);
    }

    public void RemoveHomeworkItem(HomeworkItem homeworkItem)
    {
        _logger.Information("移除作业项，ID: {Id}", homeworkItem.Id);
        var itemToRemove = HomeworkItems.Find(x => x.Id == homeworkItem.Id);
        if (itemToRemove != null)
        {
            HomeworkItems.Remove(itemToRemove);
        }
    }

    public HomeworkItem? GetHomeworkItem(Guid id)
    {
        _logger.Debug("获取作业项，ID: {Id}", id);
        return HomeworkItems.Find(x => x.Id == id);
    }

    public List<HomeworkItem> GetHomeworkItemsBySubject(string subject)
    {
        _logger.Debug("获取科目作业，科目: {Subject}", subject);
        return HomeworkItems.FindAll(x => x.Subject == subject);
    }

    public List<HomeworkItem> GetHomeworkItemsByTags(List<string> tags)
    {
        _logger.Debug("获取标签作业，标签数: {TagCount}", tags.Count);
        return HomeworkItems.Where(item => 
            item.Tags != null && 
            tags.Any(tag => item.Tags.Contains(tag))).ToList();
    }
}