using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
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

    /// <summary>
    /// 已订阅变换通知的视觉对象及其当前的 RenderTransform。
    /// 作业卡片动画直接修改祖先控件的 TranslateTransform，不会触发布局，
    /// 因此需要单独监听变换变化来同步背景镂空区域。
    /// </summary>
    private readonly Dictionary<Visual, ITransform?> _trackedRenderTransforms = [];

    private bool _isSubscribed;

    /// <summary>
    /// 材质表面的加载状态、原生材质状态或位置发生变化。主窗口据此刷新背景镂空区域。
    /// </summary>
    public static event EventHandler? MaterialStateChanged;

    /// <summary>
    /// 当前已加载的材质表面。没有背景图片时，主窗口据此在背景层上为材质区域打出透明孔洞，
    /// 从而在保留不透明主界面的同时透出原生材质；有背景图片时则用于裁剪模糊副本。
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
        StartTrackingRenderTransforms();
        MaterialStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopTrackingRenderTransforms();
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

        // IsVisible 变化时也要通知主窗口刷新背景镂空：
        // 隐藏的表面不能再在背景层留下材质空洞。
        if (change.Property == IsNativeMaterialActiveProperty ||
            change.Property == IsVisibleProperty)
            MaterialStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingChanged(bool _)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyMaterial();
        else
            Dispatcher.UIThread.Post(ApplyMaterial);
    }

    /// <summary>
    /// 监听自身及所有祖先的 RenderTransform。卡片动画直接修改 TranslateTransform 的 X/Y，
    /// 不会触发布局，只能通过变换的 Changed 事件感知位置变化。
    /// </summary>
    private void StartTrackingRenderTransforms()
    {
        StopTrackingRenderTransforms();

        for (Visual? visual = this; visual is not null; visual = visual.GetVisualParent())
        {
            visual.PropertyChanged += OnTrackedVisualPropertyChanged;
            _trackedRenderTransforms[visual] = null;
            TrackRenderTransform(visual);
        }
    }

    private void StopTrackingRenderTransforms()
    {
        foreach (var (visual, transform) in _trackedRenderTransforms)
        {
            visual.PropertyChanged -= OnTrackedVisualPropertyChanged;
            if (transform is Transform mutableTransform)
                mutableTransform.Changed -= OnTrackedRenderTransformChanged;
        }

        _trackedRenderTransforms.Clear();
    }

    private void TrackRenderTransform(Visual visual)
    {
        if (!_trackedRenderTransforms.TryGetValue(visual, out var oldTransform))
            return;

        var newTransform = visual.RenderTransform;
        if (ReferenceEquals(oldTransform, newTransform))
            return;

        if (oldTransform is Transform oldMutableTransform)
            oldMutableTransform.Changed -= OnTrackedRenderTransformChanged;
        if (newTransform is Transform newMutableTransform)
            newMutableTransform.Changed += OnTrackedRenderTransformChanged;

        _trackedRenderTransforms[visual] = newTransform;
    }

    private void OnTrackedVisualPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Visual visual)
            return;

        if (e.Property == Visual.RenderTransformProperty)
        {
            TrackRenderTransform(visual);
            RaiseMaterialPositionChanged();
        }
        else if (e.Property == Visual.RenderTransformOriginProperty)
        {
            RaiseMaterialPositionChanged();
        }
    }

    private void OnTrackedRenderTransformChanged(object? sender, EventArgs e) =>
        RaiseMaterialPositionChanged();

    private void RaiseMaterialPositionChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            MaterialStateChanged?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => MaterialStateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void ApplyMaterial()
    {
        Material = AppSettings.Instance.EnableMainViewAdvancedMaterial.Value ? SharedMaterial : null;
    }
}
