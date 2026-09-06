using System;
using System.Collections.Generic;
using System.Linq;
using ImmersingHomework.Enums;
using ImmersingHomework.Shared.Models;

namespace ImmersingHomework.Services;

public class HomeworkMergeService
{
    public static List<Guid> PreprocessHomeworksToMerge(Homework oldHomework, Homework newHomework)
    {
        var oldIds = oldHomework.HomeworkItems.Select(item => item.Id);
        var newIds = newHomework.HomeworkItems.Select(item => item.Id);
        return oldIds.Union(newIds).ToList();
    }
    
    public static Homework MergeHomework(Homework oldHomework, Homework newHomework, Dictionary<Guid, HomeworkMergeOption> options)
    {
        var conflictIds = PreprocessHomeworksToMerge(oldHomework, newHomework).ToHashSet();
        var validOptions = options
            .Where(kv => conflictIds.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var result = new Homework(newHomework.Date, []);
        foreach (var item in oldHomework.HomeworkItems)
        {
            if (validOptions.TryGetValue(item.Id, out var option))
            {
                switch (option)
                {
                    case HomeworkMergeOption.UseOld:
                        result.HomeworkItems.Add(item);
                        break;
                    case HomeworkMergeOption.UseNew:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(option));
                }
            }
            else
            {
                result.HomeworkItems.Add(item);
            }
        }

        foreach (var item in newHomework.HomeworkItems)
        {
            if (validOptions.TryGetValue(item.Id, out var option))
            {
                switch (option)
                {
                    case HomeworkMergeOption.UseOld:
                        break;
                    case HomeworkMergeOption.UseNew:
                        result.HomeworkItems.Add(item);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(option));
                }
            }
            else
            {
                result.HomeworkItems.Add(item);
            }
        }

        return result;
    }
}