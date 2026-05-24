using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

public class MacOSPlatformService : PlatformServiceBase
{
    private readonly ILogger _logger = Log.ForContext<MacOSPlatformService>();
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern void NSWindowSetLevel(IntPtr window, int level);
    
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern void NSWindowSetCollectionBehavior(IntPtr window, int behavior);

    private const int kCGWindowLevelFloating = 3;
    private const int NSWindowCollectionBehaviorIgnoreCycle = 1 << 5;

    public override void SetTopmost(Window window, bool enable = true)
    {
        window.Opened += (sender, e) =>
        {
            window.Topmost = enable;
            window.ShowInTaskbar = false;
            if (enable && window.TryGetPlatformHandle()?.Handle is IntPtr nsWindow)
            {
                NSWindowSetLevel(nsWindow, kCGWindowLevelFloating);
            }
        };
    }

    public override void DisableFocus(Window window)
    {
        window.Focusable = false;
        window.ShowActivated = false;
        
        window.Opened += (sender, e) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is IntPtr nsWindow)
            {
                NSWindowSetCollectionBehavior(nsWindow, NSWindowCollectionBehaviorIgnoreCycle);
            }
        };
    }

    public override void HideFromTaskbar(Window window)
    {
        window.ShowInTaskbar = false;
    }

    public override void HideFromAltTab(Window window)
    {
        window.ShowInTaskbar = false;
        
        window.Opened += (sender, e) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is IntPtr nsWindow)
            {
                NSWindowSetCollectionBehavior(nsWindow, NSWindowCollectionBehaviorIgnoreCycle);
            }
        };
    }
}
