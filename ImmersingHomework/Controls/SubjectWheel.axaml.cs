using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Controls;

/// <summary>
/// 科目轮盘：所有科目横向排列，当前选中的科目居中显示。
/// 默认使用设置中的科目列表，顺序与设置一致；也可以由调用方通过 ItemsSource 提供列表。
/// 支持左右滑动、直接点击任意科目以及左右方向键切换。
/// </summary>
public partial class SubjectWheel : UserControl
{
    /// <summary>每个科目的占位宽度。</summary>
    private const double ItemWidth = 132;

    /// <summary>每个科目的占位高度。</summary>
    private const double ItemHeight = 50;

    /// <summary>科目之间的间距。</summary>
    private const double ItemSpacing = 10;

    /// <summary>相邻科目中心的距离。</summary>
    private const double ItemPitch = ItemWidth + ItemSpacing;

    /// <summary>科目在画布中的垂直位置。</summary>
    private const double ItemTop = 6;

    /// <summary>横向拖动超过该距离才判定为滑动而不是点击。</summary>
    private const double DragStartThreshold = 8;

    /// <summary>已到列表两端时继续拖动，只按该比例产生回弹位移。</summary>
    private const double EdgeDragFactor = 0.22;

    private static readonly TimeSpan CenterAnimationDuration = TimeSpan.FromMilliseconds(180);

    private readonly ILogger _logger = Log.ForContext<SubjectWheel>();
    private readonly List<string> _subjects = [];
    private readonly List<Border> _itemBorders = [];
    private readonly TranslateTransform _stripTransform = new();

    private int _selectedIndex = -1;
    private bool _isLoaded;
    private bool _isSyncingSelectedSubject;
    private bool _isAttached;
    private INotifyCollectionChanged? _observedSubjectSource;

    private bool _isPointerDown;
    private bool _isDragging;
    private Point _pointerStart;
    private double _dragStartOffset;
    private int? _pressedItemIndex;

    private DispatcherTimer? _centerAnimationTimer;
    private double _animationStartOffset;
    private double _animationTargetOffset;
    private long _animationStartedAt;

    /// <summary>
    /// 当前选中的科目。支持双向绑定；外部赋值时会同步轮盘位置，
    /// 轮盘切换时也会把新值写回该属性。
    /// </summary>
    public static readonly StyledProperty<string?> SelectedSubjectProperty =
        AvaloniaProperty.Register<SubjectWheel, string?>(
            nameof(SelectedSubject),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// 可选的科目数据源。未设置时直接使用设置中的科目列表；
    /// 设置后由调用方决定列表内容（例如 ClassIsland 接管科目时）。
    /// </summary>
    public static readonly StyledProperty<IEnumerable<string>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SubjectWheel, IEnumerable<string>?>(nameof(ItemsSource));

    public string? SelectedSubject
    {
        get => GetValue(SelectedSubjectProperty);
        set => SetValue(SelectedSubjectProperty, value);
    }

    public IEnumerable<string>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>当前选中项在科目列表中的索引，没有科目时为 -1。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>当前轮盘使用的科目列表快照。</summary>
    public IReadOnlyList<string> Subjects => _subjects;

    /// <summary>选中的科目发生变化时触发。</summary>
    public event Action<string?>? SelectedSubjectChanged;

    public SubjectWheel()
    {
        _logger.Debug("SubjectWheel 初始化");
        InitializeComponent();

        SubjectCanvas.RenderTransform = _stripTransform;
        WheelRoot.SizeChanged += (_, _) =>
        {
            if (!_isPointerDown)
                CenterSelected(false);
        };

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedSubjectProperty)
            OnSelectedSubjectChanged(change.GetNewValue<string?>());
        else if (change.Property == ItemsSourceProperty && _isAttached)
        {
            SubscribeToSubjectSource();
            ReloadSubjects();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                SelectPrevious();
                e.Handled = true;
                break;
            case Key.Right:
                SelectNext();
                e.Handled = true;
                break;
            case Key.Home:
                SelectIndex(0);
                e.Handled = true;
                break;
            case Key.End:
                SelectIndex(_subjects.Count - 1);
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    /// <summary>重新读取科目列表，并尽量保持当前选中的科目。</summary>
    public void RefreshSubjects() => ReloadSubjects();

    /// <summary>选中指定名称的科目；名称不在列表中时不做任何处理。</summary>
    public void SelectSubject(string? subject)
    {
        // 尚未挂到可视树时先记到属性上，等 Attached 加载列表后再定位。
        if (!_isLoaded)
        {
            SelectedSubject = subject;
            return;
        }

        var index = subject is null ? -1 : _subjects.IndexOf(subject);
        if (index >= 0)
            SelectIndex(index);
    }

    public void SelectPrevious() => SelectIndex(_selectedIndex - 1);

    public void SelectNext() => SelectIndex(_selectedIndex + 1);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        SubscribeToSubjectSource();
        ReloadSubjects();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UnsubscribeFromSubjectSource();
        StopCenterAnimation();
        ResetPointerState();
    }

    private IEnumerable<string> GetSubjectSource() =>
        ItemsSource ?? AppSettings.Instance.Subjects;

    private void SubscribeToSubjectSource()
    {
        UnsubscribeFromSubjectSource();

        if (GetSubjectSource() is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnSubjectsCollectionChanged;
            _observedSubjectSource = collection;
        }
    }

    private void UnsubscribeFromSubjectSource()
    {
        if (_observedSubjectSource is null)
            return;

        _observedSubjectSource.CollectionChanged -= OnSubjectsCollectionChanged;
        _observedSubjectSource = null;
    }

    private void OnSubjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ReloadSubjects();
            return;
        }

        Dispatcher.UIThread.Post(ReloadSubjects);
    }

    private void ReloadSubjects()
    {
        var previousSelection = GetCurrentSubject();
        var previousSubject = previousSelection ?? SelectedSubject;

        _subjects.Clear();
        _subjects.AddRange(GetSubjectSource()
            .Where(subject => !string.IsNullOrWhiteSpace(subject))
            .Distinct(StringComparer.Ordinal));

        var index = previousSubject is null ? -1 : _subjects.IndexOf(previousSubject);
        if (index < 0 && _subjects.Count > 0)
            index = Math.Clamp(_selectedIndex, 0, _subjects.Count - 1);

        var newSelection = index >= 0 ? _subjects[index] : null;
        _selectedIndex = index;
        _isLoaded = true;

        RebuildItemVisuals();
        UpdateItemStates();
        SyncSelectedSubject();

        if (previousSelection != newSelection)
            SelectedSubjectChanged?.Invoke(newSelection);

        CenterSelected(false);

        // 首次加载时控件可能还没完成布局，等布局完成后再居中一次。
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isPointerDown)
                CenterSelected(false);
        }, DispatcherPriority.Loaded);

        _logger.Debug("科目轮盘已刷新，共 {Count} 个科目，当前索引 {Index}", _subjects.Count, _selectedIndex);
    }

    private void OnSelectedSubjectChanged(string? selectedSubject)
    {
        // 轮盘自身写回属性时不需要再次改变索引。
        if (_isSyncingSelectedSubject || !_isLoaded)
            return;

        var index = selectedSubject is null ? -1 : _subjects.IndexOf(selectedSubject);
        if (index < 0)
        {
            // 外部传入的科目不在列表中，恢复到当前有效选项。
            SyncSelectedSubject();
            return;
        }

        if (index == _selectedIndex)
        {
            CenterSelected(true);
            return;
        }

        ApplySelection(index, true);
    }

    private void SyncSelectedSubject()
    {
        var subject = GetCurrentSubject();
        if (subject == SelectedSubject)
            return;

        _isSyncingSelectedSubject = true;
        try
        {
            SetCurrentValue(SelectedSubjectProperty, subject);
        }
        finally
        {
            _isSyncingSelectedSubject = false;
        }
    }

    private void SelectIndex(int index)
    {
        if (_subjects.Count == 0 || index < 0 || index >= _subjects.Count)
            return;

        ApplySelection(index, true);
    }

    private void ApplySelection(int index, bool animate)
    {
        if (_subjects.Count == 0 || index < 0 || index >= _subjects.Count)
            return;

        var changed = index != _selectedIndex;
        _selectedIndex = index;
        UpdateItemStates();

        if (changed)
            SyncSelectedSubject();

        CenterSelected(animate);

        if (changed)
        {
            SelectedSubjectChanged?.Invoke(GetCurrentSubject());
            _logger.Debug("选中科目: {Subject}", GetCurrentSubject());
        }
    }

    private void RebuildItemVisuals()
    {
        SubjectCanvas.Children.Clear();
        _itemBorders.Clear();

        SubjectCanvas.Width = _subjects.Count == 0
            ? 0
            : (_subjects.Count - 1) * ItemPitch + ItemWidth;

        for (var i = 0; i < _subjects.Count; i++)
        {
            var text = new TextBlock
            {
                Text = _subjects[i],
                Classes = { "wheel-text" }
            };

            var border = new Border
            {
                Width = ItemWidth,
                Height = ItemHeight,
                Tag = i,
                Child = text,
                Classes = { "wheel-slot" }
            };

            Canvas.SetLeft(border, i * ItemPitch);
            Canvas.SetTop(border, ItemTop);
            SubjectCanvas.Children.Add(border);
            _itemBorders.Add(border);
        }

        EmptyHint.IsVisible = _subjects.Count == 0;
    }

    private void UpdateItemStates()
    {
        for (var i = 0; i < _itemBorders.Count; i++)
        {
            var border = _itemBorders[i];
            var distance = Math.Abs(i - _selectedIndex);

            border.Classes.Set("selected", i == _selectedIndex);
            border.Classes.Set("near", distance == 1);
            border.Opacity = i == _selectedIndex
                ? 1
                : distance == 1
                    ? 0.74
                    : distance == 2
                        ? 0.55
                        : 0.38;
        }
    }

    private string? GetCurrentSubject()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _subjects.Count)
            return null;

        return _subjects[_selectedIndex];
    }

    private double GetViewportWidth()
    {
        var width = WheelRoot.Bounds.Width;
        return width > 0 ? width : Bounds.Width;
    }

    private double GetCenteredOffset(int index) =>
        GetViewportWidth() / 2 - (index * ItemPitch + ItemWidth / 2);

    private int GetNearestIndex(double offset)
    {
        if (_subjects.Count == 0)
            return -1;

        var viewportWidth = GetViewportWidth();
        if (viewportWidth <= 0)
            return Math.Clamp(_selectedIndex, 0, _subjects.Count - 1);

        var stripCenter = viewportWidth / 2 - offset;
        var index = (int)Math.Round(
            (stripCenter - ItemWidth / 2) / ItemPitch,
            MidpointRounding.AwayFromZero);

        return Math.Clamp(index, 0, _subjects.Count - 1);
    }

    private void CenterSelected(bool animate)
    {
        if (!_isAttached || _selectedIndex < 0 || _subjects.Count == 0)
            return;

        if (GetViewportWidth() <= 0)
            return;

        var targetOffset = GetCenteredOffset(_selectedIndex);
        if (!animate)
        {
            StopCenterAnimation();
            _stripTransform.X = targetOffset;
            return;
        }

        AnimateStripTo(targetOffset);
    }

    private void AnimateStripTo(double targetOffset)
    {
        StopCenterAnimation();

        var startOffset = _stripTransform.X;
        if (Math.Abs(startOffset - targetOffset) < 0.5)
        {
            _stripTransform.X = targetOffset;
            return;
        }

        _animationStartOffset = startOffset;
        _animationTargetOffset = targetOffset;
        _animationStartedAt = Stopwatch.GetTimestamp();

        _centerAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(15)
        };
        _centerAnimationTimer.Tick += OnCenterAnimationTick;
        _centerAnimationTimer.Start();
    }

    private void OnCenterAnimationTick(object? sender, EventArgs e)
    {
        var elapsedMilliseconds =
            (Stopwatch.GetTimestamp() - _animationStartedAt) * 1000d / Stopwatch.Frequency;
        var progress = Math.Clamp(
            elapsedMilliseconds / CenterAnimationDuration.TotalMilliseconds,
            0,
            1);
        var eased = 1 - Math.Pow(1 - progress, 3);

        _stripTransform.X =
            _animationStartOffset + (_animationTargetOffset - _animationStartOffset) * eased;

        if (progress < 1)
            return;

        _stripTransform.X = _animationTargetOffset;
        StopCenterAnimation();
    }

    private void StopCenterAnimation()
    {
        if (_centerAnimationTimer is null)
            return;

        _centerAnimationTimer.Stop();
        _centerAnimationTimer.Tick -= OnCenterAnimationTick;
        _centerAnimationTimer = null;
    }

    private void WheelRoot_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Mouse &&
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        StopCenterAnimation();

        _isPointerDown = true;
        _isDragging = false;
        _pointerStart = e.GetPosition(this);
        _dragStartOffset = _stripTransform.X;
        _pressedItemIndex = GetItemIndex(e.Source as Control);

        Focus();
    }

    private void WheelRoot_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerDown)
            return;

        if (e.Pointer.Type == PointerType.Mouse &&
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetPointerState();
            CenterSelected(true);
            return;
        }

        var delta = e.GetPosition(this) - _pointerStart;

        if (!_isDragging)
        {
            if (Math.Abs(delta.X) < DragStartThreshold ||
                Math.Abs(delta.X) <= Math.Abs(delta.Y))
                return;

            _isDragging = true;
            e.Pointer.Capture(WheelRoot);
        }

        SetDragOffset(_dragStartOffset + delta.X);
        e.Handled = true;
    }

    private void WheelRoot_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerDown)
            return;

        var wasDragging = _isDragging;
        var pressedItemIndex = _pressedItemIndex;
        ResetPointerState();

        if (e.Pointer.Captured == WheelRoot)
            e.Pointer.Capture(null);

        if (wasDragging)
        {
            // 松手时选择离视口中心最近的科目，一次拖动可以跨过多个科目。
            var nearestIndex = GetNearestIndex(_stripTransform.X);
            if (nearestIndex >= 0)
                ApplySelection(nearestIndex, true);

            e.Handled = true;
            return;
        }

        // 直接点击时按实际点到的科目选中，不再依赖点击位置相对当前项的偏移。
        if (pressedItemIndex is { } itemIndex)
        {
            ApplySelection(itemIndex, true);
            e.Handled = true;
        }
    }

    private void WheelRoot_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isPointerDown)
            return;

        ResetPointerState();
        CenterSelected(true);
    }

    private void ResetPointerState()
    {
        _isPointerDown = false;
        _isDragging = false;
        _pressedItemIndex = null;
    }

    private void SetDragOffset(double offset)
    {
        if (_subjects.Count == 0)
        {
            _stripTransform.X = 0;
            return;
        }

        var maxOffset = GetCenteredOffset(0);
        var minOffset = GetCenteredOffset(_subjects.Count - 1);

        if (offset > maxOffset)
            offset = maxOffset + (offset - maxOffset) * EdgeDragFactor;
        else if (offset < minOffset)
            offset = minOffset + (offset - minOffset) * EdgeDragFactor;

        _stripTransform.X = offset;
    }

    private int? GetItemIndex(Control? source)
    {
        var current = source;
        while (current is not null && current != WheelRoot)
        {
            if (current is Border { Tag: int index } &&
                index >= 0 &&
                index < _itemBorders.Count &&
                ReferenceEquals(_itemBorders[index], current))
            {
                return index;
            }

            current = current.Parent as Control;
        }

        return null;
    }
}
