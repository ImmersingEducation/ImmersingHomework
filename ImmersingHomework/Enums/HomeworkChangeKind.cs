namespace ImmersingHomework.Enums;

/// <summary>
/// 作业内容的变化类型，用于决定主界面刷新时播放的卡片动画。
/// </summary>
public enum HomeworkChangeKind
{
    /// <summary>普通刷新，不播放作业项变化动画。</summary>
    None,

    /// <summary>新增作业，新卡片从右侧滑入。</summary>
    Added,

    /// <summary>编辑作业，旧卡片向左滑出，新卡片从右侧滑入。</summary>
    Edited,

    /// <summary>删除作业，旧卡片向左滑出，剩余内容上移。</summary>
    Deleted
}
