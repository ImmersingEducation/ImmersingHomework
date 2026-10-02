using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ImmersingHomework.Shared.Models;

namespace ImmersingHomework.Abstractions;

/// <summary>按日期读写作业数据，并在内容变更时自动生成快照。</summary>
public interface IHomeworkStorageService
{
    bool Exists(DateOnly date);

    void Save(Homework homework);

    Task SaveAsync(Homework homework);

    Homework? Load(DateOnly date);

    Task<Homework?> LoadAsync(DateOnly date);

    Homework? LoadFromFile(string filePath);

    List<DateOnly> GetAllHomeworkDates();

    bool Delete(DateOnly date);

    /// <summary>删除截止日期之前以及内容为空的作业文件，返回删除的文件数量。</summary>
    int DeleteBeforeAndEmpty(DateTimeOffset cutoffDate);
}