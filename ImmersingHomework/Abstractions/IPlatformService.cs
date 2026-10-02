using Avalonia.Controls;

namespace ImmersingHomework.Abstractions;

/// <summary>
/// 当前运行平台的系统能力（窗口置顶与隐藏、开机自启、系统通知、URL 协议注册、快捷方式等）。
/// 各平台实现位于 <c>ImmersingHomework.Services.Platforms</c>，由 DI 容器按操作系统选择注册。
/// </summary>
public interface IPlatformService
{
    void SetTopmost(Window window, bool enable = true);

    void DisableFocus(Window window);

    void HideFromTaskbar(Window window);

    void HideFromAltTab(Window window);

    void SetLaunchAtStartup(bool enabled);

    void SendNotification(string title, string message);

    bool IsUrlSchemaRegistered { get; set; }

    void CreateDesktopShortcut();

    void CreateStartMenuShortcut();

    void ShowDesktopShortcut();
}