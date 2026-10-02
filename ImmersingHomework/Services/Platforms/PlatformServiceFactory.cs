using System;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services.Platforms;

/// <summary>
/// 按当前操作系统与 Linux 会话类型挑选具体的 <see cref="IPlatformService"/> 实现，
/// 供 DI 容器以工厂方式注册使用。
/// </summary>
public static class PlatformServiceFactory
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PlatformServiceFactory));

    /// <summary>创建当前运行平台对应的平台服务；平台不受支持时抛出异常。</summary>
    public static IPlatformService Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatformService();
        if (OperatingSystem.IsMacOS())
            return new MacOSPlatformService();
        if (OperatingSystem.IsLinux())
        {
            var xdgSession = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (!string.IsNullOrEmpty(xdgSession))
            {
                if (xdgSession.Equals("x11", StringComparison.OrdinalIgnoreCase))
                    return new X11PlatformService();
                if (xdgSession.Equals("wayland", StringComparison.OrdinalIgnoreCase))
                    return new WaylandPlatformService();
            }

            var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (!string.IsNullOrEmpty(waylandDisplay))
                return new WaylandPlatformService();

            var display = Environment.GetEnvironmentVariable("DISPLAY");
            if (!string.IsNullOrEmpty(display))
                return new X11PlatformService();

            return new X11PlatformService(); // 默认使用 X11
        }

        Logger.Error("当前操作系统不受支持: {Os}", Environment.OSVersion);
        throw new PlatformNotSupportedException();
    }
}