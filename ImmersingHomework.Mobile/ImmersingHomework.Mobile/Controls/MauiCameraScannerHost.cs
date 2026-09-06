using Avalonia.Controls;
using Avalonia.Platform;

namespace ImmersingHomework.Mobile.Controls;

/// <summary>
/// Hosts the platform native camera scanner view (backed by the MAUI
/// <c>ZXing.Net.Maui.Controls.CameraBarcodeReaderView</c>) inside the Avalonia
/// visual tree.
/// </summary>
public class MauiCameraScannerHost : NativeControlHost
{
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        return MauiCameraScannerBridge.CreateNativeControl();
    }
}
