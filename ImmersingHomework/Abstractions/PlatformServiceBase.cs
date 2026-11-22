using Avalonia.Controls;
using Serilog;

namespace ImmersingHomework.Abstractions;

public abstract class PlatformServiceBase : IPlatformService
{
    protected readonly ILogger Logger = Log.ForContext<PlatformServiceBase>();
    public abstract void SetTopmost(Window window, bool enable =  true);

    public virtual void SetBottommost(Window window, bool enable = true)
    {
        // 通用实现只能保证窗口不置顶；支持原生置底的平台会覆盖此方法。
        window.Topmost = false;
        Logger.Debug("当前平台未实现窗口置底，已取消置顶");
    }

    public abstract void DisableFocus(Window window);
    public abstract void HideFromTaskbar(Window window);
    public abstract void HideFromAltTab(Window window);

    public abstract void SetLaunchAtStartup(bool enabled);

    public virtual void SendNotification(string title, string message)
    {
        Logger.Warning("当前平台未实现系统通知: {Title} - {Message}", title, message);
    }

    public abstract bool IsUrlSchemaRegistered { get; set; }

    public abstract void CreateDesktopShortcut();
    public abstract void CreateStartMenuShortcut();
    public abstract void ShowDesktopShortcut();
}