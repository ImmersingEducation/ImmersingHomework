using System.Collections.Generic;
using ClassIsland.Shared.Models.Profile;

namespace ImmersingHomework.Abstractions;

/// <summary>与 ClassIsland 的 IPC 联动：课表、科目以及课间/放学/上课通知。</summary>
public interface IClassIslandService
{
    /// <summary>是否已初始化（注册通知处理程序并尝试建立 IPC 连接）。</summary>
    bool Initialized { get; }

    void Initialize();

    List<string> GetSubjects();

    /// <summary>当前时间是否在第一节课之前。</summary>
    bool IsCurrentTimeBeforeFirstClass();

    /// <summary>获取上一节课的科目，未能获取时返回 <c>null</c>。</summary>
    Subject? GetPreviousClassSubject();
}