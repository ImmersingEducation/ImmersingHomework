using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ImmersingHomework.Abstractions;
using Serilog;

namespace ImmersingHomework.Services;

[SupportedOSPlatform("macos")]
internal sealed class MacOSUrlSchemeService : IMacOSUrlSchemeService
{
    private const uint kEventClassInternet = 0x4755524C; // 'GURL'
    private const uint kAEGetURL = 0x4755524C;            // 'GURL'
    private const uint keyDirectObject = 0x2D2D2D2D;      // '----'
    private const uint typeAEURL = 0x75726C20;            // 'url '

    private readonly ILogger _logger = Log.ForContext<MacOSUrlSchemeService>();
    private Action<string>? _handler;
    private IntPtr _handlerPtr;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AEEventHandler(IntPtr appleEvent, IntPtr reply, long refCon);

    private readonly AEEventHandler _urlEventDelegate;

    [DllImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")]
    private static extern int AEInstallEventHandler(
        uint eventClass, uint eventId, IntPtr handler, long handlerRefcon,
        [MarshalAs(UnmanagedType.I1)] bool isSysHandler);

    [DllImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")]
    private static extern int AEGetParamPtr(
        IntPtr theAppleEvent, uint keyword, uint desiredType, out uint actualType,
        IntPtr dataPtr, int maximumSize, out int actualSize);

    public MacOSUrlSchemeService()
    {
        _urlEventDelegate = UrlEventHandler;
    }

    public void Install(Action<string> onUrl)
    {
        _handler = onUrl;

        if (_handlerPtr == IntPtr.Zero)
            _handlerPtr = Marshal.GetFunctionPointerForDelegate(_urlEventDelegate);

        var result = AEInstallEventHandler(kEventClassInternet, kAEGetURL, _handlerPtr, 0, false);
        if (result != 0)
            _logger.Warning("安装 URL 协议 Apple Event 处理器失败，错误码: {Code}", result);
        else
            _logger.Information("已安装 URL 协议 Apple Event 处理器");
    }

    private int UrlEventHandler(IntPtr appleEvent, IntPtr reply, long refCon)
    {
        try
        {
            var url = GetUrl(appleEvent);
            if (!string.IsNullOrEmpty(url))
                _handler?.Invoke(url);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理 Apple Event URL 失败");
        }

        return 0; // noErr
    }

    private string? GetUrl(IntPtr appleEvent)
    {
        uint actualType;
        int actualSize;

        var status = AEGetParamPtr(appleEvent, keyDirectObject, typeAEURL, out actualType, IntPtr.Zero, 0, out actualSize);
        if (status != 0 || actualSize <= 0)
        {
            _logger.Warning("获取 Apple Event URL 大小失败，状态: {Status}", status);
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