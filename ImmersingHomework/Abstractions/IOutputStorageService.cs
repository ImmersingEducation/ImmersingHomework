using System;

namespace ImmersingHomework.Abstractions;

/// <summary>导出产物（Outputs 目录）的清理。</summary>
public interface IOutputStorageService
{
    /// <summary>删除截止日期之前的导出文件，返回删除的文件数量。</summary>
    int DeleteBefore(DateTimeOffset cutoffDate);
}