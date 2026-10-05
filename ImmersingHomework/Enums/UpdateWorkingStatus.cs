namespace ImmersingHomework.Enums;

/// <summary>当前正在执行的更新任务，用于驱动进度指示、状态图标旋转以及按钮的可用状态。</summary>
public enum UpdateWorkingStatus
{
    /// <summary>空闲，可以发起检查 / 下载等操作。</summary>
    Idle = 0,

    /// <summary>正在连接更新服务器检查更新。</summary>
    CheckingUpdates,

    /// <summary>正在下载更新包。</summary>
    DownloadingUpdates
}