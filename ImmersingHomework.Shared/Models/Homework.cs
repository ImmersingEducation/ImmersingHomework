using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ImmersingHomework.Shared.Models;

public class Homework
{
    private readonly ILogger _logger = Log.ForContext<Homework>();
    public DateOnly Date { get; init; }
    public bool Frozen { get; set; } = false;
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

    /// <summary>
    /// 用同一 ID 的新作业项替换原作业项，并保留它在列表中的位置。
    /// 找不到原作业项时返回 <see langword="false"/>，列表保持不变。
    /// </summary>
    public bool ReplaceHomeworkItem(HomeworkItem homeworkItem)
    {
        _logger.Information("替换作业项，ID: {Id}", homeworkItem.Id);
        var index = HomeworkItems.FindIndex(x => x.Id == homeworkItem.Id);
        if (index < 0)
        {
            _logger.Warning("替换作业项失败，未找到原作业项，ID: {Id}", homeworkItem.Id);
            return false;
        }

        HomeworkItems[index] = homeworkItem;
        return true;
    }

    /// <summary>
    /// 整理作业项：同一科目的作业项保持原有相对顺序聚在一起，
    /// 科目按 <paramref name="subjectOrder"/> 的顺序排列，不在其中的科目排在最后。
    /// </summary>
    public void OrganizeHomeworkItems(IEnumerable<string> subjectOrder)
    {
        var subjectRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var subject in subjectOrder)
        {
            if (!string.IsNullOrEmpty(subject) && !subjectRanks.ContainsKey(subject))
                subjectRanks[subject] = subjectRanks.Count;
        }

        int GetRank(string? subject) =>
            subject is not null && subjectRanks.TryGetValue(subject, out var rank) ? rank : int.MaxValue;

        var orderedItems = HomeworkItems
            .Select((item, index) => (Item: item, Index: index))
            .OrderBy(x => GetRank(x.Item.Subject))
            .ThenBy(x => x.Index)
            .Select(x => x.Item)
            .ToList();

        HomeworkItems.Clear();
        HomeworkItems.AddRange(orderedItems);
        _logger.Debug("作业项已按科目整理，共 {Count} 项", HomeworkItems.Count);
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
            tags.Any(tag => item.Tags.Any(t => t.Name == tag))).ToList();
    }
}
