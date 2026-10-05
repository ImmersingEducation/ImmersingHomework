namespace ImmersingHomework.Enums;

/// <summary>更新状态机，驱动设置页顶部的状态图标、提示文案以及操作按钮的可见性。</summary>
public enum UpdateStatus
{
    /// <summary>尚未检查更新，或上次检查未得到有效结果。</summary>
    Unknown = 0,

    /// <summary>当前已是最新版本。</summary>
    UpToDate,

    /// <summary>发现新版本，等待下载。</summary>
    UpdateAvailable,

    /// <summary>更新包已下载完成，等待重启软件应用。</summary>
    UpdateDownloaded
}