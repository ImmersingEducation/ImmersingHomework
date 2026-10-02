using System;
using System.Collections.Generic;
using ImmersingHomework.Enums;
using ImmersingHomework.Shared.Models;

namespace ImmersingHomework.Abstractions;

/// <summary>两份作业（当前作业与快照）之间的差异比对与合并。</summary>
public interface IHomeworkMergeService
{
    /// <summary>返回两份作业中所有出现过的作业项 ID。</summary>
    List<Guid> PreprocessHomeworksToMerge(Homework oldHomework, Homework newHomework);

    /// <summary>按冲突选项合并两份作业；未提供选项的冲突项以旧作业为准。</summary>
    Homework MergeHomework(Homework oldHomework, Homework newHomework,
        Dictionary<Guid, HomeworkMergeOption> options);
}