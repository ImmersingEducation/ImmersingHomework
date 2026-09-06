using System;
using Avalonia.Platform;

namespace ImmersingHomework.Mobile;

public sealed class BarcodeDetectedEventArgs : EventArgs
{
    public BarcodeDetectedEventArgs(string value)
    {
        Value = value;
    }

    public string Value { get; }
}

/// <summary>
/// Platform-agnostic bridge between the Avalonia UI and the platform specific
/// MAUI camera scanner implementation. The Android/iOS entry projects register
/// their implementation during startup via <see cref="Initialize"/>.
/// </summary>
public static class MauiCameraScannerBridge
{
    private static Func<IPlatformHandle?>? _createNativeControl;

    public static event EventHandler<BarcodeDetectedEventArgs>? BarcodeDetected;

    /// <summary>
    /// Registers the platform implementation. <paramref name="createNativeControl"/>
    /// must create the MAUI <c>CameraBarcodeReaderView</c>, convert it to a native
    /// view handle and forward its <c>BarcodesDetected</c> events by calling
    /// <see cref="RaiseBarcodeDetected"/>.
    /// </summary>
    public static void Initialize(Func<IPlatformHandle?> createNativeControl)
    {
        _createNativeControl = createNativeControl ?? throw new ArgumentNullException(nameof(createNativeControl));
    }

    public static bool IsInitialized => _createNativeControl is not null;

    public static IPlatformHandle CreateNativeControl()
    {
        if (_createNativeControl is null)
            throw new InvalidOperationException("MauiCameraScannerBridge has not been initialized on this platform.");

        return _createNativeControl() ?? throw new InvalidOperationException("The platform failed to create the MAUI camera scanner native control.");
    }

    public static void RaiseBarcodeDetected(string value)
    {
        BarcodeDetected?.Invoke(null, new BarcodeDetectedEventArgs(value));
    }
}
