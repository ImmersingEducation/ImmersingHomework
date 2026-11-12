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
    /// 返回科目首次出现的顺序。科目在界面中的先后与分列位置直接由
    /// <see cref="HomeworkItems"/> 中的作业项顺序表示，不额外保存字段。
    /// </summary>
    public List<string> GetSubjectsInOrder()
    {
        return HomeworkItems
            .Select(item => item.Subject)
            .Where(subject => !string.IsNullOrEmpty(subject))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 按给定科目顺序重新排列作业项。同一科目的作业项保持原有相对顺序，
    /// 未出现在 <paramref name="subjectOrder"/> 中的科目按首次出现顺序排在最后。
    /// </summary>
    public void ReorderSubjects(IEnumerable<string> subjectOrder)
    {
        var subjectOrderList = subjectOrder
            .Where(subject => !string.IsNullOrEmpty(subject))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var groups = new Dictionary<string, List<HomeworkItem>>(StringComparer.Ordinal);
        foreach (var item in HomeworkItems)
        {
            if (!groups.TryGetValue(item.Subject, out var group))
            {
                group = [];
                groups[item.Subject] = group;
            }

            group.Add(item);
        }

        var reordered = new List<HomeworkItem>(HomeworkItems.Count);
        foreach (var subject in subjectOrderList)
        {
            if (groups.Remove(subject, out var group))
                reordered.AddRange(group);
        }

        // 不在指定顺序中的科目保持它们第一次出现时的先后关系。
        foreach (var item in HomeworkItems)
        {
            if (groups.Remove(item.Subject, out var group))
                reordered.AddRange(group);
        }

        HomeworkItems.Clear();
        HomeworkItems.AddRange(reordered);
        _logger.Debug("作业项已按科目顺序重组，共 {Count} 项", HomeworkItems.Count);
    }

    /// <summary>
    /// 把指定科目移动到科目顺序中的 <paramref name="targetSubjectIndex"/> 位置。
    /// 返回 <see langword="false"/> 表示科目不存在或位置没有变化。
    /// </summary>
    public bool MoveSubject(string subject, int targetSubjectIndex)
    {
        var subjects = GetSubjectsInOrder();
        var currentIndex = subjects.IndexOf(subject);
        if (currentIndex < 0)
            return false;

        subjects.RemoveAt(currentIndex);
        var targetIndex = Math.Clamp(targetSubjectIndex, 0, subjects.Count);
        if (targetIndex == currentIndex)
            return false;

        subjects.Insert(targetIndex, subject);
        ReorderSubjects(subjects);
        return true;
    }

    /// <summary>
    /// 把指定作业项移动到它所属科目的第 <paramref name="targetIndexWithinSubject"/> 个位置。
    /// 其他科目的作业项和科目顺序保持原样；返回 <see langword="false"/> 表示作业项不存在或位置没有变化。
    /// </summary>
    public bool MoveHomeworkItem(Guid id, int targetIndexWithinSubject)
    {
        var item = HomeworkItems.Find(x => x.Id == id);
        if (item is null)
            return false;

        var subject = item.Subject;
        var subjects = GetSubjectsInOrder();
        var groups = HomeworkItems
            .GroupBy(x => x.Subject, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        if (!groups.TryGetValue(subject, out var subjectItems))
            return false;

        var currentIndex = subjectItems.IndexOf(item);
        subjectItems.Remove(item);
        var targetIndex = Math.Clamp(targetIndexWithinSubject, 0, subjectItems.Count);
        if (targetIndex == currentIndex)
            return false;

        subjectItems.Insert(targetIndex, item);

        var reordered = new List<HomeworkItem>(HomeworkItems.Count);
        foreach (var groupSubject in subjects)
        {
            if (string.Equals(groupSubject, subject, StringComparison.Ordinal))
                reordered.AddRange(subjectItems);
            else if (groups.TryGetValue(groupSubject, out var group))
                reordered.AddRange(group);
        }

        // 没有有效科目的作业项保持原有位置，避免在整理时丢失。
        reordered.AddRange(HomeworkItems.Where(x =>
            string.IsNullOrEmpty(x.Subject) && !reordered.Contains(x)));

        HomeworkItems.Clear();
        HomeworkItems.AddRange(reordered);
        _logger.Debug("作业项已移动，科目: {Subject}，目标位置: {Index}", subject, targetIndex);
        return true;
    }

    /// <summary>
    /// 整理作业项：同一科目的作业项保持原有相对顺序聚在一起，
    /// 并保持科目当前的先后顺序不变。用于编辑作业后不破坏用户拖拽出的排列。
    /// </summary>
    public void OrganizeHomeworkItems() => OrganizeHomeworkItems(GetSubjectsInOrder());

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
