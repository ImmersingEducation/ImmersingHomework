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
}