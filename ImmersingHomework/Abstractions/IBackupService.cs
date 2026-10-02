using System.Collections.Generic;
using ImmersingHomework.Shared.Models;

namespace ImmersingHomework.Abstractions;

/// <summary>作业数据的备份（打包）与恢复（解包），支持软件私有格式与 UAF 格式。</summary>
public interface IBackupService
{
    /// <summary>把作业文件打包成 zip，返回压缩包路径。</summary>
    string PackHomeworks(List<string> homeworkPaths);

    /// <summary>把作业文件转换成 UAF 格式的 PDF，返回生成的 PDF 路径。</summary>
    List<string> PackHomeworksAsUaf(List<string> homeworkPaths);

    /// <summary>从软件私有格式的备份包中恢复作业。</summary>
    List<Homework> UnpackHomeworks(string packagePath);

    /// <summary>从 UAF 格式的 PDF 中恢复作业。</summary>
    List<Homework> UnpackHomeworksAsUaf(List<string> homeworkPaths);
}