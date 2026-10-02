using System;

namespace ImmersingHomework.Abstractions;

/// <summary>日志文件（Logs 目录）的清理。</summary>
public interface ILogStorageService
{
    /// <summary>删除截止日期之前的日志文件，返回删除的文件数量。</summary>
    int DeleteBefore(DateTimeOffset cutoffDate);
}