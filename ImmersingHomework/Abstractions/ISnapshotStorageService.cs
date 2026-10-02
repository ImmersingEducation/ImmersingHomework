using System;
using System.Collections.Generic;
using ImmersingHomework.Services;

namespace ImmersingHomework.Abstractions;

/// <summary>作业快照的查询与清理。</summary>
public interface ISnapshotStorageService
{
    List<SnapshotInfo> GetSnapshots(DateOnly date);

    long GetStorageUsage();

    int ClearAll();

    /// <summary>删除截止日期之前的快照，返回删除的文件数量。</summary>
    int ClearBefore(DateTimeOffset cutoffDate);
}