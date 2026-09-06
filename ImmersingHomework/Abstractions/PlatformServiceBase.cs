using Avalonia.Controls;
using Serilog;

namespace ImmersingHomework.Abstractions;

public abstract class PlatformServiceBase
{
    protected readonly ILogger Logger = Log.ForContext<PlatformServiceBase>();
    public abstract void SetTopmost(Window window, bool enable =  true);
    public abstract void DisableFocus(Window window);
    public abstract void HideFromTaskbar(Window window);
    public abstract void HideFromAltTab(Window window);

    public abstract void SetLaunchAtStartup(bool enabled);

    public virtual void SendNotification(string title, string message)
    {
        Logger.Warning("当前平台未实现系统通知: {Title} - {Message}", title, message);
    }
}