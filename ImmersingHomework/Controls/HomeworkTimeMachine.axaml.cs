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
using ImmersingHomework.Services;
using Serilog;

namespace ImmersingHomework.Controls;

/// <summary>
/// 作业时光机：当天所有作业快照按时间从早到晚横向排列，当前选中的快照居中显示。
/// 支持左右滑动、直接点击任意快照以及左右方向键切换。
/// </summary>
public partial class HomeworkTimeMachine : UserControl
{
    /// <summary>每个快照的占位宽度。</summary>
    private const double ItemWidth = 210;

    /// <summary>每个快照的占位高度。</summary>
    private const double ItemHeight = 50;

    /// <summary>快照之间的间距。</summary>
    private const double ItemSpacing = 10;

    /// <summary>相邻快照中心的距离。</summary>
    private const double ItemPitch = ItemWidth + ItemSpacing;

    /// <summary>快照在画布中的垂直位置。</summary>
    private const double ItemTop = 6;

    /// <summary>横向拖动超过该距离才判定为滑动而不是点击。</summary>
    private const double DragStartThreshold = 8;

    /// <summary>已到列表两端时继续拖动，只按该比例产生回弹位移。</summary>
    private const double EdgeDragFactor = 0.22;

    private static readonly TimeSpan CenterAnimationDuration = TimeSpan.FromMilliseconds(180);

    private readonly ILogger _logger = Log.ForContext<HomeworkTimeMachine>();
    private readonly List<SnapshotInfo> _snapshots = [];
    private readonly List<Border> _itemBorders = [];
    private readonly TranslateTransform _stripTransform = new();

    private int _selectedIndex = -1;
    private bool _isLoaded;
    private bool _isSyncingSelectedSnapshot;
    private bool _isAttached;
    private INotifyCollectionChanged? _observedSnapshotSource;

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
    /// 当前选中的快照。支持双向绑定；外部赋值时会同步时光机位置，
    /// 时光机切换时也会把新值写回该属性。
    /// </summary>
    public static readonly StyledProperty<SnapshotInfo?> SelectedSnapshotProperty =
        AvaloniaProperty.Register<HomeworkTimeMachine, SnapshotInfo?>(
            nameof(SelectedSnapshot),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// 快照数据源。调用方负责提供当天作业的所有快照，控件内部会按创建时间从早到晚排序。
    /// </summary>
    public static readonly StyledProperty<IEnumerable<SnapshotInfo>?> ItemsSourceProperty =
        AvaloniaProperty.Register<HomeworkTimeMachine, IEnumerable<SnapshotInfo>?>(nameof(ItemsSource));

    public SnapshotInfo? SelectedSnapshot
    {
        get => GetValue(SelectedSnapshotProperty);
        set => SetValue(SelectedSnapshotProperty, value);
    }

    public IEnumerable<SnapshotInfo>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>当前选中项在快照列表中的索引，没有快照时为 -1。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>当前时光机使用的快照列表，已按创建时间从早到晚排列。</summary>
    public IReadOnlyList<SnapshotInfo> Snapshots => _snapshots;

    /// <summary>选中的快照发生变化时触发。</summary>
    public event Action<SnapshotInfo?>? SelectedSnapshotChanged;

    public HomeworkTimeMachine()
    {
        _logger.Debug("HomeworkTimeMachine 初始化");
        InitializeComponent();

        SnapshotCanvas.RenderTransform = _stripTransform;
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

        if (change.Property == SelectedSnapshotProperty)
            OnSelectedSnapshotChanged(change.GetNewValue<SnapshotInfo?>());
        else if (change.Property == ItemsSourceProperty && _isAttached)
        {
            SubscribeToSnapshotSource();
            ReloadSnapshots();
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
                SelectLatest();
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    /// <summary>重新读取快照列表，并尽量保持当前选中的快照。</summary>
    public void RefreshSnapshots() => ReloadSnapshots();

    /// <summary>选中指定快照；快照不在列表中时不做任何处理。</summary>
    public void SelectSnapshot(SnapshotInfo? snapshot)
    {
        // 尚未挂到可视树时先记到属性上，等 Attached 加载列表后再定位。
        if (!_isLoaded)
        {
            SelectedSnapshot = snapshot;
            return;
        }

        var index = snapshot is null ? -1 : FindSnapshotIndex(snapshot);
        if (index >= 0)
            SelectIndex(index);
    }

    /// <summary>选中最新（最靠右）的快照。</summary>
    public void SelectLatest()
    {
        if (_snapshots.Count == 0)
            return;

        SelectIndex(_snapshots.Count - 1);
    }

    public void SelectPrevious() => SelectIndex(_selectedIndex - 1);

    public void SelectNext() => SelectIndex(_selectedIndex + 1);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        SubscribeToSnapshotSource();
        ReloadSnapshots();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UnsubscribeFromSnapshotSource();
        StopCenterAnimation();
        ResetPointerState();
    }

    private IEnumerable<SnapshotInfo> GetSnapshotSource() =>
        ItemsSource ?? Enumerable.Empty<SnapshotInfo>();

    private void SubscribeToSnapshotSource()
    {
        UnsubscribeFromSnapshotSource();

        if (GetSnapshotSource() is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnSnapshotsCollectionChanged;
            _observedSnapshotSource = collection;
        }
    }

    private void UnsubscribeFromSnapshotSource()
    {
        if (_observedSnapshotSource is null)
            return;

        _observedSnapshotSource.CollectionChanged -= OnSnapshotsCollectionChanged;
        _observedSnapshotSource = null;
    }

    private void OnSnapshotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ReloadSnapshots();
            return;
        }

        Dispatcher.UIThread.Post(ReloadSnapshots);
    }

    private void ReloadSnapshots()
    {
        var previousSelection = GetCurrentSnapshot() ?? SelectedSnapshot;

        _snapshots.Clear();
        _snapshots.AddRange(GetSnapshotSource()
            .Where(snapshot => snapshot is not null)
            .OrderBy(snapshot => snapshot.CreatedAt)
            .ThenBy(snapshot => snapshot.FilePath, StringComparer.Ordinal));

        var index = previousSelection is null ? -1 : FindSnapshotIndex(previousSelection);

        // 找不到之前选中的快照时默认选中最新的一条。
        if (index < 0 && _snapshots.Count > 0)
            index = _snapshots.Count - 1;

        var newSelection = index >= 0 ? _snapshots[index] : null;
        _selectedIndex = index;
        _isLoaded = true;

        RebuildItemVisuals();
        UpdateItemStates();
        SyncSelectedSnapshot();

        if (!Equals(previousSelection, newSelection))
            SelectedSnapshotChanged?.Invoke(newSelection);

        CenterSelected(false);

        // 首次加载时控件可能还没完成布局，等布局完成后再居中一次。
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isPointerDown)
                CenterSelected(false);
        }, DispatcherPriority.Loaded);

        _logger.Debug("时光机已刷新，共 {Count} 个快照，当前索引 {Index}", _snapshots.Count, _selectedIndex);
    }

    private void OnSelectedSnapshotChanged(SnapshotInfo? selectedSnapshot)
    {
        // 时光机自身写回属性时不需要再次改变索引。
        if (_isSyncingSelectedSnapshot || !_isLoaded)
            return;

        var index = selectedSnapshot is null ? -1 : FindSnapshotIndex(selectedSnapshot);
        if (index < 0)
        {
            // 外部传入的快照不在列表中，恢复到当前有效选项。
            SyncSelectedSnapshot();
            return;
        }

        if (index == _selectedIndex)
        {
            CenterSelected(true);
            return;
        }

        ApplySelection(index, true);
    }

    private void SyncSelectedSnapshot()
    {
        var snapshot = GetCurrentSnapshot();
        if (Equals(snapshot, SelectedSnapshot))
            return;

        _isSyncingSelectedSnapshot = true;
        try
        {
            SetCurrentValue(SelectedSnapshotProperty, snapshot);
        }
        finally
        {
            _isSyncingSelectedSnapshot = false;
        }
    }

    private void SelectIndex(int index)
    {
        if (_snapshots.Count == 0 || index < 0 || index >= _snapshots.Count)
            return;

        ApplySelection(index, true);
    }

    private void ApplySelection(int index, bool animate)
    {
        if (_snapshots.Count == 0 || index < 0 || index >= _snapshots.Count)
            return;

        var changed = index != _selectedIndex;
        _selectedIndex = index;
        UpdateItemStates();

        if (changed)
            SyncSelectedSnapshot();

        CenterSelected(animate);

        if (changed)
        {
            SelectedSnapshotChanged?.Invoke(GetCurrentSnapshot());
            _logger.Debug("选中快照: {Snapshot}", FormatSnapshot(GetCurrentSnapshot()));
        }
    }

    private void RebuildItemVisuals()
    {
        SnapshotCanvas.Children.Clear();
        _itemBorders.Clear();

        SnapshotCanvas.Width = _snapshots.Count == 0
            ? 0
            : (_snapshots.Count - 1) * ItemPitch + ItemWidth;

        for (var i = 0; i < _snapshots.Count; i++)
        {
            var snapshot = _snapshots[i];
            var text = new TextBlock
            {
                Text = FormatSnapshot(snapshot),
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
            ToolTip.SetTip(border, $"{FormatSnapshot(snapshot)}\n点击选择这个时间点");

            Canvas.SetLeft(border, i * ItemPitch);
            Canvas.SetTop(border, ItemTop);
            SnapshotCanvas.Children.Add(border);
            _itemBorders.Add(border);
        }

        EmptyHint.IsVisible = _snapshots.Count == 0;
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

    private SnapshotInfo? GetCurrentSnapshot()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _snapshots.Count)
            return null;

        return _snapshots[_selectedIndex];
    }

    private int FindSnapshotIndex(SnapshotInfo snapshot)
    {
        for (var i = 0; i < _snapshots.Count; i++)
        {
            if (string.Equals(_snapshots[i].FilePath, snapshot.FilePath, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static string FormatSnapshot(SnapshotInfo? snapshot) =>
        snapshot?.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

    private double GetViewportWidth()
    {
        var width = WheelRoot.Bounds.Width;
        return width > 0 ? width : Bounds.Width;
    }

    private double GetCenteredOffset(int index) =>
        GetViewportWidth() / 2 - (index * ItemPitch + ItemWidth / 2);

    private int GetNearestIndex(double offset)
    {
        if (_snapshots.Count == 0)
            return -1;

        var viewportWidth = GetViewportWidth();
        if (viewportWidth <= 0)
            return Math.Clamp(_selectedIndex, 0, _snapshots.Count - 1);

        var stripCenter = viewportWidth / 2 - offset;
        var index = (int)Math.Round(
            (stripCenter - ItemWidth / 2) / ItemPitch,
            MidpointRounding.AwayFromZero);

        return Math.Clamp(index, 0, _snapshots.Count - 1);
    }

    private void CenterSelected(bool animate)
    {
        if (!_isAttached || _selectedIndex < 0 || _snapshots.Count == 0)
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
            // 松手时选择离视口中心最近的快照，一次拖动可以跨过多个快照。
            var nearestIndex = GetNearestIndex(_stripTransform.X);
            if (nearestIndex >= 0)
                ApplySelection(nearestIndex, true);

            e.Handled = true;
            return;
        }

        // 直接点击时按实际点到的快照选中，不再依赖点击位置相对当前项的偏移。
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
        if (_snapshots.Count == 0)
        {
            _stripTransform.X = 0;
            return;
        }

        var maxOffset = GetCenteredOffset(0);
        var minOffset = GetCenteredOffset(_snapshots.Count - 1);

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
