using Android.App;
using Avalonia.Android;
using Avalonia.Platform;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace ImmersingHomework.Mobile.Android;

public static class MauiCameraScanner
{
    private static MauiApp? _mauiApp;
    private static MauiContext? _mauiContext;
    private static CameraBarcodeReaderView? _cameraView;

    public static void Initialize(Activity activity)
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<MauiApplication>();
        builder.UseBarcodeReader();
        _mauiApp = builder.Build();

        _mauiContext = new MauiContext(_mauiApp.Services, activity);

        MauiCameraScannerBridge.Initialize(CreateNativeControl);
    }

    private static IPlatformHandle? CreateNativeControl()
    {
        if (_mauiContext is null)
            return null;

        _cameraView = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode,
                AutoRotate = true,
            },
            IsDetecting = true,
        };

        _cameraView.BarcodesDetected += (_, e) =>
        {
            foreach (var result in e.Results)
            {
                if (!string.IsNullOrEmpty(result.Value))
                    MauiCameraScannerBridge.RaiseBarcodeDetected(result.Value);
            }
        };

        var nativeView = _cameraView.ToPlatform(_mauiContext);
        return new AndroidViewControlHandle(nativeView);
    }
}

public sealed class MauiApplication : Microsoft.Maui.Controls.Application
{
}
