using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CompositionMaterial.Avalonia;
using CompositionMaterial.Avalonia.Materials;
using ImmersingHomework.Models;

namespace ImmersingHomework.Controls;

/// <summary>
/// 主界面高级材质表面。开关关闭时只绘制 <see cref="CompositionMaterialControl.FallbackBrush"/>，
/// 开启时使用共享的 Mica 材质；设置变更后无需重启即可切换。
/// </summary>
public class AdvancedMaterialSurface : CompositionMaterialControl
{
    private static readonly MicaMaterial SharedMaterial = new() { ThemeMode = MicaThemeMode.Auto };
    private static readonly HashSet<AdvancedMaterialSurface> LoadedSurfaces = [];

    private bool _isSubscribed;

    /// <summary>
    /// 材质表面的加载状态或原生材质状态发生变化。主窗口据此刷新背景镂空区域。
    /// </summary>
    public static event EventHandler? MaterialStateChanged;

    /// <summary>
    /// 当前已加载的材质表面。主窗口据此在背景层上为材质区域打出透明孔洞，
    /// 从而在保留不透明主界面的同时透出原生材质。
    /// </summary>
    public static IReadOnlyCollection<AdvancedMaterialSurface> GetLoadedSurfaces() => LoadedSurfaces;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        LoadedSurfaces.Add(this);
        if (!_isSubscribed)
        {
            AppSettings.Instance.EnableMainViewAdvancedMaterial.ValueChanged += OnSettingChanged;
            _isSubscribed = true;
        }

        ApplyMaterial();
        MaterialStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LoadedSurfaces.Remove(this);
        if (_isSubscribed)
        {
            AppSettings.Instance.EnableMainViewAdvancedMaterial.ValueChanged -= OnSettingChanged;
            _isSubscribed = false;
        }

        MaterialStateChanged?.Invoke(this, EventArgs.Empty);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsNativeMaterialActiveProperty)
            MaterialStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingChanged(bool _)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyMaterial();
        else
            Dispatcher.UIThread.Post(ApplyMaterial);
    }

    private void ApplyMaterial()
    {
        Material = AppSettings.Instance.EnableMainViewAdvancedMaterial.Value ? SharedMaterial : null;
    }
}
