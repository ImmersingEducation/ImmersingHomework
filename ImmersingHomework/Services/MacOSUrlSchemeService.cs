using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace ImmersingHomework.Services;

[SupportedOSPlatform("macos")]
internal static class MacOSUrlSchemeService
{
    private const uint kEventClassInternet = 0x4755524C; // 'GURL'
    private const uint kAEGetURL = 0x4755524C;            // 'GURL'
    private const uint keyDirectObject = 0x2D2D2D2D;      // '----'
    private const uint typeAEURL = 0x75726C20;            // 'url '

    private static readonly ILogger Logger = Log.ForContext(typeof(MacOSUrlSchemeService));
    private static Action<string>? _handler;
    private static IntPtr _handlerPtr;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AEEventHandler(IntPtr appleEvent, IntPtr reply, long refCon);

    private static readonly AEEventHandler UrlEventDelegate = UrlEventHandler;

    [DllImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")]
    private static extern int AEInstallEventHandler(
        uint eventClass, uint eventId, IntPtr handler, long handlerRefcon,
        [MarshalAs(UnmanagedType.I1)] bool isSysHandler);

    [DllImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")]
    private static extern int AEGetParamPtr(
        IntPtr theAppleEvent, uint keyword, uint desiredType, out uint actualType,
        IntPtr dataPtr, int maximumSize, out int actualSize);

    /// <summary>安装 kAEGetURL 事件处理器，收到 URL 时回调 <paramref name="onUrl"/>。</summary>
    public static void Install(Action<string> onUrl)
    {
        _handler = onUrl;

        if (_handlerPtr == IntPtr.Zero)
            _handlerPtr = Marshal.GetFunctionPointerForDelegate(UrlEventDelegate);

        var result = AEInstallEventHandler(kEventClassInternet, kAEGetURL, _handlerPtr, 0, false);
        if (result != 0)
            Logger.Warning("安装 URL 协议 Apple Event 处理器失败，错误码: {Code}", result);
        else
            Logger.Information("已安装 URL 协议 Apple Event 处理器");
    }

    private static int UrlEventHandler(IntPtr appleEvent, IntPtr reply, long refCon)
    {
        try
        {
            var url = GetUrl(appleEvent);
            if (!string.IsNullOrEmpty(url))
                _handler?.Invoke(url);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "处理 Apple Event URL 失败");
        }

        return 0; // noErr
    }

    private static string? GetUrl(IntPtr appleEvent)
    {
        uint actualType;
        int actualSize;

        var status = AEGetParamPtr(appleEvent, keyDirectObject, typeAEURL, out actualType, IntPtr.Zero, 0, out actualSize);
        if (status != 0 || actualSize <= 0)
        {
            Logger.Warning("获取 Apple Event URL 大小失败，状态: {Status}", status);
            return null;
        }

        var buffer = Marshal.AllocHGlobal(actualSize);
        try
        {
            status = AEGetParamPtr(appleEvent, keyDirectObject, typeAEURL, out actualType, buffer, actualSize, out actualSize);
            if (status != 0) return null;

            return Marshal.PtrToStringUTF8(buffer, actualSize)?.TrimEnd('\0');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
