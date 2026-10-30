using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Windowing;

namespace ImmersingHomework.Helper;

/// <summary>
/// 修复 Windows 下 FluentAvalonia 窗口标题栏无法拖动的问题。
///
/// Avalonia 12 通过命中测试视觉树来决定扩展客户区顶部是否为可拖动区域。
/// FluentAvalonia 3.1 的标题栏没有参与这套命中测试（自定义标题栏也可能没有设置
/// ElementRole），于是 Windows 收到的是 HTCLIENT，窗口就无法通过标题栏移动。
/// 这里直接挂接窗口的 WndProc，在标题栏区域返回 HTCAPTION，同时跳过标题栏里的
/// 按钮、输入框等交互控件，避免影响正常点击。
/// </summary>
internal static class WindowsTitleBarDrag
{
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const double ResizeBorder = 6.0;

    private static readonly ConditionalWeakTable<Window, Hook> Hooks = new();

    /// <summary>
    /// 在应用启动时调用一次，为之后打开的所有 FAAppWindow 挂接修复逻辑。
    /// </summary>
    public static void Register()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Attach(window));
    }

    private static void Attach(Window window)
    {
        if (window is not FAAppWindow || Hooks.TryGetValue(window, out _))
            return;

        var hook = new Hook(window);
        Hooks.Add(window, hook);
        hook.Install();
        window.Closed += (_, _) => hook.Uninstall();
    }

    private sealed class Hook
    {
        private readonly Window _window;
        private readonly Win32Properties.CustomWndProcHookCallback _callback;

        public Hook(Window window)
        {
            _window = window;
            _callback = OnWndProc;
        }

        public void Install() =>
            Win32Properties.AddWndProcHookCallback(_window, _callback);

        public void Uninstall() =>
            Win32Properties.RemoveWndProcHookCallback(_window, _callback);

        private nint OnWndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST || !TryGetTitleBarPoint(_window, lParam, out var point))
                return 0;

            // 标题栏里可能有按钮、文本框等控件，这些位置必须交还给 Avalonia。
            if (IsInteractiveAt(_window, point))
                return 0;

            handled = true;
            return HTCAPTION;
        }
    }

    private static bool TryGetTitleBarPoint(Window window, nint lParam, out Point point)
    {
        point = default;

        if (window.WindowState == WindowState.FullScreen ||
            !window.IsVisible ||
            window.GetVisualParent() is null ||
            window.ClientSize.Width <= 0 ||
            window.ClientSize.Height <= 0)
        {
            return false;
        }

        var screenPoint = new PixelPoint(
            (short)(lParam & 0xFFFF),
            (short)((lParam >> 16) & 0xFFFF));

        try
        {
            point = window.PointToClient(screenPoint);
        }
        catch (Exception)
        {
            // 窗口尚未完成布局时命中测试没有意义，交给系统默认处理。
            return false;
        }

        var titleBarHeight = GetTitleBarHeight(window);
        if (titleBarHeight <= 0)
            return false;

        // 顶部/左右边缘留给系统调整窗口大小；最大化时没有可调整的边框。
        var border = window.WindowState == WindowState.Maximized ? 0 : ResizeBorder;
        if (point.Y < border ||
            point.Y >= titleBarHeight ||
            point.Y >= window.ClientSize.Height ||
            point.X < border ||
            point.X > window.ClientSize.Width - border)
        {
            return false;
        }

        return true;
    }

    private static double GetTitleBarHeight(Window window)
    {
        if (window is FAAppWindow appWindow)
        {
            var height = appWindow.TitleBar.Height;
            if (appWindow.ExtendClientAreaTitleBarHeightHint > height)
                height = appWindow.ExtendClientAreaTitleBarHeightHint;

            return height > 0 ? height : 32;
        }

        return 32;
    }

    private static bool IsInteractiveAt(Window window, Point point)
    {
        // Window 的视觉父级是 TopLevelHost，标题栏按钮画在它的 Overlay 层里，
        // 从 Window 自己开始命中测试是找不到这些按钮的。
        var root = window.GetVisualParent() ?? (Visual)window;

        var hit = root.GetVisualAt(
            point,
            visual => visual is IInputElement { IsHitTestVisible: true, IsEffectivelyVisible: true });

        for (var current = hit; current is not null && current != window; current = current.GetVisualParent())
        {
            var role = WindowDecorationProperties.GetElementRole(current);
            if (role is not WindowDecorationsElementRole.None and not WindowDecorationsElementRole.TitleBar)
                return true;

            if (current is Button or TextBox or ComboBox or CheckBox or RadioButton or ToggleButton or MenuItem or Slider or ListBoxItem)
                return true;

            if (current is Control { Focusable: true })
                return true;
        }

        return false;
    }
}
