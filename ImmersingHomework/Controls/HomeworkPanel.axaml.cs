using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class  HomeworkPanel : UserControl
{
    /// <summary>
    /// HomeworkGrid 的列数。每一列是一个独立的 StackPanel，各列高度互不影响。
    /// 取值来自应用设置，允许范围为 1~6。
    /// </summary>
    private int HomeworkColumnCount => Math.Clamp(AppSettings.Instance.MainViewColumns.Value, 1, 6);

    private readonly List<StackPanel> _homeworkColumns = [];
    private readonly List<SubjectHomeworkPanel> _subjectPanels = [];

    /// <summary>
    /// 内容版本号。异步刷新开始时记录版本，加载完成后若版本已变化，
    /// 说明期间已经直接展示了快照或开始了更新的刷新，旧结果必须丢弃。
    /// </summary>
    private int _contentVersion;

    /// <summary>
    /// 自动滚动的速度（像素/秒）。
    /// </summary>
    private const double AutoScrollSpeed = 50;

    /// <summary>
    /// 自动滚动的刷新间隔，按帧更新以保证滚动平滑。
    /// </summary>
    private static readonly TimeSpan AutoScrollFrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _autoScrollTimer = new(DispatcherPriority.Render)
    {
        Interval = AutoScrollFrameInterval
    };

    private readonly Stopwatch _autoScrollStopwatch = new();
    private bool _isAutoScrollEnabled;

    /// <summary>
    /// 当前自动滚动方向：1 表示向下，-1 表示向上。
    /// </summary>
    private int _autoScrollDirection = 1;

    #region 拖拽模式

    private enum DragSourceKind
    {
        None,
        Subject,
        HomeworkItem
    }

    /// <summary>
    /// 拖拽开始的鼠标位移阈值。低于该阈值时仍视为普通点击。
    /// </summary>
    private const double DragStartThreshold = 4;

    /// <summary>
    /// 拖拽时距离视口上下边缘多少像素开始自动滚动。
    /// </summary>
    private const double DragAutoScrollEdge = 64;

    /// <summary>
    /// 拖拽时的自动滚动速度（像素/秒）。
    /// </summary>
    private const double DragAutoScrollSpeed = 320;

    private bool _isDragModeEnabled;
    private DragSourceKind _dragCandidateKind;
    private SubjectHomeworkPanel? _dragCandidateSubject;
    private HomeworkItemPanel? _dragCandidateItem;
    private Point _dragStartPoint;
    private bool _isPointerTracking;
    private bool _isDragging;
    private IPointer? _dragPointer;
    private Control? _dragSourceControl;
    private SubjectHomeworkPanel? _dragSourceSubject;
    private HomeworkItemPanel? _dragSourceItem;
    private Control? _dragGhost;
    private Border? _dropIndicator;
    private bool _hasDropTarget;
    private int _dropTargetColumn = -1;
    private int _dropTargetSlot;
    private int _dropTargetItemIndex;
    private Point _dragLastPoint;

    private readonly DispatcherTimer _dragAutoScrollTimer = new(DispatcherPriority.Render)
    {
        Interval = AutoScrollFrameInterval
    };

    private readonly Stopwatch _dragAutoScrollStopwatch = new();

    #endregion

    private readonly ILogger _logger = Log.ForContext<HomeworkPanel>();
    public static readonly StyledProperty<DateOnly> DateProperty =
        AvaloniaProperty.Register<HomeworkPanel, DateOnly>(nameof(Date));

    public event Action<DateOnly>? DateChanged;

    public static readonly StyledProperty<bool> IsFrozenProperty =
        AvaloniaProperty.Register<HomeworkPanel, bool>(nameof(IsFrozen), false);

    public event Action<bool>? FrozenChanged;

    /// <summary>
    /// 用户点击了某个作业项，请求编辑该作业。由 <see cref="Views.MainWindow"/> 订阅并打开主抽屉。
    /// </summary>
    public event Action<HomeworkItem>? EditRequested;

    public bool IsFrozen
    {
        get => GetValue(IsFrozenProperty);
        set => SetValue(IsFrozenProperty, value);
    }

    public DateOnly Date
    {
        get => GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    /// <summary>
    /// 是否启用自动滚动。启用后内容从上向下滚动，到达底部后反向向上，循环往复。
    /// </summary>
    public bool IsAutoScrollEnabled
    {
        get => _isAutoScrollEnabled;
        set
        {
            if (_isAutoScrollEnabled == value)
                return;

            _isAutoScrollEnabled = value;
            if (value)
                StartAutoScroll();
            else
                StopAutoScroll();
        }
    }

    /// <summary>
    /// 是否启用拖拽模式。启用后科目标题和作业卡片可以拖拽排序。
    /// </summary>
    public bool IsDragModeEnabled
    {
        get => _isDragModeEnabled;
        set
        {
            if (_isDragModeEnabled == value)
                return;

            _isDragModeEnabled = value;
            if (!value)
                CancelDrag();

            foreach (var subjectPanel in _subjectPanels)
                subjectPanel.IsDragModeEnabled = value;
        }
    }

    private IHomeworkStorageService? _storageService;
    private IClassIslandService? _classIslandService;
    private readonly bool _autoLoad;

    public HomeworkPanel() : this(true)
    {
    }

    public HomeworkPanel(bool autoLoad)
    {
        _logger.Information("HomeworkPanel 初始化开始");
        _autoLoad = autoLoad;
        InitializeComponent();
        _autoScrollTimer.Tick += AutoScrollTimer_OnTick;
        _dragAutoScrollTimer.Tick += DragAutoScrollTimer_OnTick;
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;

        // 使用隧道事件并启用 handledEventsToo，确保作业卡片自身处理点击后
        // 拖拽模式仍能从卡片和科目标题开始拖拽。
        AddHandler(PointerPressedEvent, HomeworkPanel_OnPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, HomeworkPanel_OnPointerMoved, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, HomeworkPanel_OnPointerReleased, RoutingStrategies.Tunnel, true);
        AddHandler(PointerCaptureLostEvent, HomeworkPanel_OnPointerCaptureLost, RoutingStrategies.Direct);
        DateProperty.Changed.AddClassHandler<HomeworkPanel>((panel, e) =>
        {
            _logger.Debug("日期改变: {Date}", panel.Date);
            panel.DateChanged?.Invoke(panel.Date);
            // 依赖服务尚未注入时（如 XAML 预览）跳过加载，待 Initialize 注入后再补
            if (panel._storageService is not null)
                _ = panel.RefreshAsync(panel.Date);
        });
        IsFrozenProperty.Changed.AddClassHandler<HomeworkPanel>((panel, e) =>
        {
            panel.ApplyFrozenStateToSubjects();
            panel.FrozenChanged?.Invoke(panel.IsFrozen);
        });
        _logger.Information("HomeworkPanel 初始化完成");
    }

    private IHomeworkStorageService StorageService =>
        _storageService ?? throw new InvalidOperationException("HomeworkPanel 尚未注入 IHomeworkStorageService。");

    private IClassIslandService ClassIslandService =>
        _classIslandService ?? throw new InvalidOperationException("HomeworkPanel 尚未注入 IClassIslandService。");

    /// <summary>
    /// 注入依赖服务。本控件在 <c>MainWindow.axaml</c> 中以 XAML 方式声明创建，
    /// 构造函数由 XAML 编译器调用，无法传入服务，因此由 <see cref="Views.MainWindow"/>
    /// 在 <c>InitializeComponent</c> 之后显式注入。
    /// </summary>
    public void Initialize(IHomeworkStorageService storageService, IClassIslandService classIslandService)
    {
        _storageService = storageService;
        _classIslandService = classIslandService;
        _logger.Debug("HomeworkPanel 依赖服务注入完成");

        if (_autoLoad)
            Date = DateOnly.FromDateTime(DateTime.Now);
    }

    public void DisplayHomework(Homework homework)
    {
        _logger.Debug("直接展示作业内容，日期: {Date}", homework.Date);
        // 直接展示会覆盖异步刷新的结果，使所有尚未完成的刷新失效。
        _contentVersion++;
        HomeworkGrid.IsVisible = false;
        ClearHomeworkGrid();
        IsFrozen = homework.Frozen;

        var hasHomework = false;
        if (homework.HomeworkItems is { Count: > 0 })
        {
            foreach (var subject in homework.GetSubjectsInOrder())
            {
                var subjectItems = homework.GetHomeworkItemsBySubject(subject);
                if (subjectItems is { Count: > 0 })
                {
                    var subjectPanel = new SubjectHomeworkPanel();
                    subjectPanel.SetData(subject, subjectItems);
                    subjectPanel.EditRequested += OnEditRequested;
                    subjectPanel.IsFrozen = IsFrozen;
                    subjectPanel.IsDragModeEnabled = IsDragModeEnabled;
                    AddSubjectPanel(subjectPanel);
                    hasHomework = true;
                }
            }
        }

        HomeworkGrid.IsVisible = true;
        NoHomeworkText.IsVisible = !hasHomework;
        _logger.Debug("作业内容展示完成");
    }

    public void Refresh()
    {
        _ = RefreshAsync(Date);
    }

    public async Task RefreshAsync(DateOnly date)
    {
#if DEBUG
        var sw = Stopwatch.StartNew();
#endif
        _logger.Debug("刷新作业面板，日期: {Date}", date);
        var contentVersion = ++_contentVersion;
        HomeworkGrid.IsVisible = false;
        ClearHomeworkGrid();
        var homework = await StorageService.LoadAsync(date);

        // 加载期间如果直接展示了快照或开始了更新的刷新，当前结果已经过期。
        if (contentVersion != _contentVersion)
        {
            _logger.Debug("作业面板刷新已过期，丢弃结果，日期: {Date}", date);
            return;
        }

        IsFrozen = homework?.Frozen ?? false;

        var hasHomework = false;
        if (homework != null)
        {
            // 只在文件不存在且是空作业时才创建文件，避免重复保存
            if ((homework.HomeworkItems == null || homework.HomeworkItems.Count == 0) 
                && !StorageService.Exists(date))
            {
                _logger.Debug("自动创建空作业文件，日期: {Date}", date);
                // 自动保存这个空白作业，创建对应的日期文件（异步执行，不阻塞UI）
                Task.Run(async () =>
                {
                    try
                    {
                        await StorageService.SaveAsync(homework);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "保存空作业时出错");
                    }
                });
            }
            else if (homework.HomeworkItems != null && homework.HomeworkItems.Count > 0)
            {
                var subjects = homework.GetSubjectsInOrder();

                _logger.Debug("找到 {Count} 个科目，日期: {Date}", subjects.Count, date);

                foreach (var subject in subjects)
                {
                    if (!string.IsNullOrEmpty(subject))
                    {
                        var subjectItems = homework.GetHomeworkItemsBySubject(subject);
                        if (subjectItems != null && subjectItems.Count > 0)
                        {
                            var subjectPanel = new SubjectHomeworkPanel();
                            subjectPanel.SetData(subject, subjectItems);
                            subjectPanel.EditRequested += OnEditRequested;
                            subjectPanel.IsFrozen = IsFrozen;
                            subjectPanel.IsDragModeEnabled = IsDragModeEnabled;
                            AddSubjectPanel(subjectPanel);
                            hasHomework = true;
                        }
                    }
                }
            }
        }

        HomeworkGrid.IsVisible = true;

        if (NoHomeworkText != null)
        {
            NoHomeworkText.IsVisible = !hasHomework;
        }
        
        _logger.Debug("作业面板刷新完成，有作业: {HasHomework}", hasHomework);
#if DEBUG
        sw.Stop();
        _logger.Debug("Refresh 方法执行耗时: {Elapsed}ms", sw.Elapsed.TotalMilliseconds);
#endif
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // 先移除再添加，避免控件反复进出视觉树时重复订阅。
        AppSettings.Instance.MainViewColumns.ValueChanged -= OnMainViewColumnsChanged;
        AppSettings.Instance.MainViewColumns.ValueChanged += OnMainViewColumnsChanged;

        // 控件重新挂回视觉树时，按最新列数排列已经加载的内容。
        if (_homeworkColumns.Count > 0)
            RebuildHomeworkColumns();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AppSettings.Instance.MainViewColumns.ValueChanged -= OnMainViewColumnsChanged;
        CancelDrag();
    }

    private void OnMainViewColumnsChanged(int _)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RebuildHomeworkColumns);
            return;
        }

        RebuildHomeworkColumns();
    }

    /// <summary>
    /// 按当前列数重新分配已有的科目面板，不重新读取作业内容，也不改变科目顺序。
    /// 科目顺序由 <see cref="Homework.HomeworkItems"/> 的作业项顺序决定。
    /// </summary>
    private void RebuildHomeworkColumns()
    {
        var subjectPanels = _subjectPanels.ToList();

        ClearHomeworkGrid();
        foreach (var subjectPanel in subjectPanels)
            AddSubjectPanel(subjectPanel);

        _logger.Debug("主界面排列变更，重新排列 {Count} 个科目面板", subjectPanels.Count);
    }

    private void ClearHomeworkGrid()
    {
        // 作业内容即将重建，正在进行的拖拽不再有意义，先复位浮层和源卡片透明度。
        CancelDrag();

        // 先清空每列中的科目面板，确保它们与旧列的父子关系被解除，
        // 否则重新分列时会被判定为“已有视觉父级”。
        foreach (var column in _homeworkColumns)
            column.Children.Clear();

        HomeworkGrid.Children.Clear();
        HomeworkGrid.ColumnDefinitions.Clear();
        HomeworkGrid.RowDefinitions.Clear();
        _homeworkColumns.Clear();
        _subjectPanels.Clear();

        for (var column = 0; column < HomeworkColumnCount; column++)
        {
            HomeworkGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var columnPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 32,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(columnPanel, column);
            HomeworkGrid.Children.Add(columnPanel);
            _homeworkColumns.Add(columnPanel);
        }
    }

    private void AddSubjectPanel(SubjectHomeworkPanel subjectPanel)
    {
        _subjectPanels.Add(subjectPanel);
        ReflowSubjectPanels();
    }

    /// <summary>
    /// 按科目顺序把科目面板连续分配到各列。第一列优先获得多出的科目，
    /// 这样作业项列表的顺序就是从左到右、从上到下的阅读顺序，
    /// 拖拽科目时可以直接用列表位置表示目标列和列内位置。
    /// </summary>
    private void ReflowSubjectPanels()
    {
        foreach (var column in _homeworkColumns)
            column.Children.Clear();

        var columnCount = _homeworkColumns.Count;
        if (columnCount == 0)
            return;

        var subjectCount = _subjectPanels.Count;
        var columnSizes = GetColumnSizes(subjectCount, columnCount);
        var subjectIndex = 0;

        for (var column = 0; column < columnCount; column++)
        {
            for (var i = 0; i < columnSizes[column] && subjectIndex < subjectCount; i++)
                _homeworkColumns[column].Children.Add(_subjectPanels[subjectIndex++]);
        }
    }

    private static int[] GetColumnSizes(int subjectCount, int columnCount)
    {
        var sizes = new int[columnCount];
        var baseSize = subjectCount / columnCount;
        var extra = subjectCount % columnCount;
        for (var column = 0; column < columnCount; column++)
            sizes[column] = baseSize + (column < extra ? 1 : 0);

        return sizes;
    }

    private static int GetColumnStartIndex(IReadOnlyList<int> columnSizes, int column)
    {
        var index = 0;
        for (var i = 0; i < column && i < columnSizes.Count; i++)
            index += columnSizes[i];

        return index;
    }

    private void ApplyFrozenStateToSubjects()
    {
        foreach (var subjectPanel in _subjectPanels)
            subjectPanel.IsFrozen = IsFrozen;
    }

    #region 拖拽排序

    private void HomeworkPanel_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isDragModeEnabled || IsFrozen || _isDragging)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is not Visual source)
            return;

        // 只有科目标题能拖动整个科目，科目内部的作业卡片仍用于拖动单条作业。
        var subjectPanel = FindVisualAncestor<SubjectHomeworkPanel>(source);
        if (subjectPanel is not null && IsDescendantOf(source, subjectPanel.SubjectDragHandle))
        {
            BeginDragCandidate(DragSourceKind.Subject, subjectPanel, null, e);
            return;
        }

        var itemPanel = FindVisualAncestor<HomeworkItemPanel>(source);
        if (itemPanel is not null)
        {
            var ownerSubject = FindVisualAncestor<SubjectHomeworkPanel>(itemPanel);
            if (ownerSubject is not null)
                BeginDragCandidate(DragSourceKind.HomeworkItem, ownerSubject, itemPanel, e);
        }
    }

    private void BeginDragCandidate(
        DragSourceKind kind,
        SubjectHomeworkPanel subjectPanel,
        HomeworkItemPanel? itemPanel,
        PointerEventArgs e)
    {
        _dragCandidateKind = kind;
        _dragCandidateSubject = subjectPanel;
        _dragCandidateItem = itemPanel;
        _dragStartPoint = e.GetPosition(this);
        _dragLastPoint = _dragStartPoint;
        _dragPointer = e.Pointer;
        _isPointerTracking = true;

        // 按下时就捕获指针，保证拖动距离较短或移出面板时仍能收到松开事件，
        // 从而可靠地区分“点击编辑”和“拖拽排序”。吞掉按下事件，避免
        // ScrollViewer 等父级手势同时捕获指针。
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void HomeworkPanel_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerTracking)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CancelDrag();
            return;
        }

        var point = e.GetPosition(this);
        if (!_isDragging)
        {
            var offsetX = point.X - _dragStartPoint.X;
            var offsetY = point.Y - _dragStartPoint.Y;
            if (offsetX * offsetX + offsetY * offsetY < DragStartThreshold * DragStartThreshold)
                return;

            StartDrag(e, point);
        }

        if (!_isDragging)
            return;

        _dragLastPoint = point;
        UpdateDragGhostPosition(point);
        UpdateDropTarget(point);
        e.Handled = true;
    }

    private void HomeworkPanel_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerTracking)
            return;

        var point = e.GetPosition(this);
        if (_isDragging)
        {
            _dragLastPoint = point;
            UpdateDropTarget(point);

            var kind = _dragCandidateKind;
            var hasDropTarget = _hasDropTarget;
            var sourceSubject = _dragSourceSubject;
            var sourceItem = _dragSourceItem;
            var targetColumn = _dropTargetColumn;
            var targetSlot = _dropTargetSlot;
            var targetItemIndex = _dropTargetItemIndex;

            EndDrag(e.Pointer);
            e.Handled = true;

            if (!hasDropTarget || sourceSubject is null)
                return;

            if (kind == DragSourceKind.HomeworkItem && sourceItem is not null)
                ApplyHomeworkItemDrop(sourceItem, targetItemIndex);
            else if (kind == DragSourceKind.Subject)
                ApplySubjectDrop(sourceSubject, targetColumn, targetSlot);
            return;
        }

        // 没有达到拖拽阈值，按普通点击处理：作业卡片打开编辑，科目标题不响应。
        var clickedKind = _dragCandidateKind;
        var clickedItem = _dragCandidateItem;
        CancelDrag();

        if (clickedKind == DragSourceKind.HomeworkItem && clickedItem is not null)
            clickedItem.RequestEdit();
    }

    private void HomeworkPanel_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging || _isPointerTracking)
            CancelDrag();
    }

    private void StartDrag(PointerEventArgs e, Point point)
    {
        if (!_isDragModeEnabled || IsFrozen ||
            _dragCandidateKind == DragSourceKind.None ||
            _dragCandidateSubject is null ||
            (_dragCandidateKind == DragSourceKind.HomeworkItem && _dragCandidateItem is null))
        {
            CancelDrag();
            return;
        }

        _dragSourceSubject = _dragCandidateSubject;
        _dragSourceItem = _dragCandidateItem;
        _dragSourceControl = _dragCandidateKind == DragSourceKind.Subject
            ? _dragCandidateSubject
            : _dragCandidateItem;
        _isDragging = true;
        _dragLastPoint = point;

        // 拖拽期间暂停普通自动滚动，避免两个计时器同时修改滚动位置。
        _autoScrollTimer.Stop();
        _autoScrollStopwatch.Stop();

        // 原始内容淡化，提示用户当前拖拽的是哪一项。
        _dragSourceControl!.Opacity = 0.3;

        _dropIndicator = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false,
            IsVisible = false
        };
        _dropIndicator.Bind(Border.BackgroundProperty,
            new DynamicResourceExtension("SystemFillColorAttentionBrush"));
        DragOverlay.Children.Add(_dropIndicator);

        _dragGhost = CreateDragGhost();
        if (_dragGhost is not null)
        {
            DragOverlay.Children.Add(_dragGhost);
            UpdateDragGhostPosition(point);
        }

        e.Pointer.Capture(this);
        _dragAutoScrollStopwatch.Restart();
        _dragAutoScrollTimer.Start();
        _logger.Debug("开始拖拽，类型: {Kind}，科目: {Subject}", _dragCandidateKind,
            _dragSourceSubject.Subject);
    }

    private Control? CreateDragGhost()
    {
        if (_dragSourceItem is not null)
        {
            var width = _dragSourceItem.Bounds.Width;
            if (!double.IsFinite(width) || width <= 0)
                width = 280;

            return new HomeworkItemPanel
            {
                HomeworkItem = _dragSourceItem.HomeworkItem,
                Width = Math.Clamp(width, 220, 360),
                Opacity = 0.94,
                IsHitTestVisible = false
            };
        }

        if (_dragSourceSubject is null)
            return null;

        var ghostContent = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4
        };
        ghostContent.Children.Add(new TextBlock
        {
            Text = _dragSourceSubject.Subject,
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280
        });
        ghostContent.Children.Add(new TextBlock
        {
            Text = $"{_dragSourceSubject.ItemPanelHost.Children.Count} 项作业",
            FontSize = 13
        });

        var ghost = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Opacity = 0.94,
            IsHitTestVisible = false,
            Child = ghostContent
        };
        ghost.Bind(Border.BackgroundProperty,
            new DynamicResourceExtension("CardBackgroundFillColorDefaultBrush"));
        ghost.Bind(Border.BorderBrushProperty,
            new DynamicResourceExtension("CardStrokeColorDefaultBrush"));
        return ghost;
    }

    private void UpdateDragGhostPosition(Point point)
    {
        if (_dragGhost is null)
            return;

        var width = _dragGhost.Bounds.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = _dragGhost.DesiredSize.Width;

        var height = _dragGhost.Bounds.Height;
        if (!double.IsFinite(height) || height <= 0)
            height = _dragGhost.DesiredSize.Height;

        // 卡片跟随指针移动，靠近窗口边缘时向内收，避免被浮层裁剪。
        var left = point.X + 16;
        var top = point.Y + 16;
        if (width > 0)
            left = Math.Clamp(left, 4, Math.Max(4, Bounds.Width - width - 8));
        if (height > 0)
            top = Math.Clamp(top, 4, Math.Max(4, Bounds.Height - height - 8));

        Canvas.SetLeft(_dragGhost, left);
        Canvas.SetTop(_dragGhost, top);
    }

    private void UpdateDropTarget(Point point)
    {
        _hasDropTarget = false;
        _dropTargetColumn = -1;
        _dropTargetSlot = 0;
        _dropTargetItemIndex = 0;

        if (!_isDragging || _dragSourceSubject is null)
        {
            HideDropIndicator();
            return;
        }

        // 拖出内容区域时不显示落点，松手即取消。
        if (point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > Bounds.Height)
        {
            HideDropIndicator();
            return;
        }

        if (_dragCandidateKind == DragSourceKind.HomeworkItem)
            UpdateHomeworkItemDropTarget(point);
        else
            UpdateSubjectDropTarget(point);

        if (!_hasDropTarget)
            HideDropIndicator();
    }

    private void UpdateHomeworkItemDropTarget(Point point)
    {
        if (_dragSourceSubject is null || _dragSourceItem is null)
            return;

        var subjectTopLeft = _dragSourceSubject.TranslatePoint(new Point(0, 0), this);
        if (subjectTopLeft is null)
            return;

        var subjectLeft = subjectTopLeft.Value.X;
        var subjectTop = subjectTopLeft.Value.Y;
        var subjectWidth = _dragSourceSubject.Bounds.Width;
        var subjectHeight = _dragSourceSubject.Bounds.Height;

        // 作业卡片只在所属科目的范围内排序，离开所属科目一定距离后不再显示落点。
        const double subjectBoundsTolerance = 12;
        if (point.X < subjectLeft - subjectBoundsTolerance ||
            point.X > subjectLeft + subjectWidth + subjectBoundsTolerance ||
            point.Y < subjectTop - subjectBoundsTolerance ||
            point.Y > subjectTop + subjectHeight + subjectBoundsTolerance)
            return;

        // 使用包含源卡片的完整布局计算分隔线，保证线始终落在卡片之间的空隙，
        // 再把完整布局中的插入位置换算成“移除源卡片后”的索引。
        var placements = new List<(HomeworkItemPanel Panel, double Top, double Bottom)>();
        foreach (var panel in _dragSourceSubject.ItemPanelHost.Children.OfType<HomeworkItemPanel>())
        {
            var topLeft = panel.TranslatePoint(new Point(0, 0), this);
            if (topLeft is null)
                continue;

            placements.Add((panel, topLeft.Value.Y, topLeft.Value.Y + panel.Bounds.Height));
        }

        var sourceIndex = placements.FindIndex(x => ReferenceEquals(x.Panel, _dragSourceItem));
        if (sourceIndex < 0 || placements.Count == 0)
            return;

        var fullInsertionIndex = placements.Count;
        for (var i = 0; i < placements.Count; i++)
        {
            var center = (placements[i].Top + placements[i].Bottom) / 2;
            if (point.Y < center)
            {
                fullInsertionIndex = i;
                break;
            }
        }

        var insertionIndex = fullInsertionIndex > sourceIndex
            ? fullInsertionIndex - 1
            : fullInsertionIndex;

        var lineY = fullInsertionIndex <= 0
            ? placements[0].Top - 6
            : fullInsertionIndex >= placements.Count
                ? placements[^1].Bottom + 6
                : (placements[fullInsertionIndex - 1].Bottom + placements[fullInsertionIndex].Top) / 2;

        _dropTargetItemIndex = insertionIndex;
        _hasDropTarget = true;
        ShowDropIndicator(subjectLeft, lineY, subjectWidth);
    }

    private void UpdateSubjectDropTarget(Point point)
    {
        if (_dragSourceSubject is null || _homeworkColumns.Count == 0)
            return;

        // 只允许落到当前实际有科目的列，避免拖到空白区域后结果不可预测。
        var targetColumn = -1;
        var bestDistance = double.MaxValue;
        var maxColumn = Math.Min(_homeworkColumns.Count, _subjectPanels.Count);
        for (var column = 0; column < maxColumn; column++)
        {
            var topLeft = _homeworkColumns[column].TranslatePoint(new Point(0, 0), this);
            if (topLeft is null)
                continue;

            var left = topLeft.Value.X;
            var right = left + _homeworkColumns[column].Bounds.Width;
            var distance = point.X < left
                ? left - point.X
                : point.X > right
                    ? point.X - right
                    : 0;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                targetColumn = column;
            }
        }

        if (targetColumn < 0 || bestDistance > 64)
            return;

        var columns = _homeworkColumns[targetColumn];
        var columnTopLeft = columns.TranslatePoint(new Point(0, 0), this);
        if (columnTopLeft is null)
            return;

        // 与作业卡片相同：用包含源科目的完整布局定位分隔线，再换算成
        // 目标列中移除源科目后的插入位置。
        var placements = new List<(SubjectHomeworkPanel Panel, double Top, double Bottom)>();
        foreach (var panel in columns.Children.OfType<SubjectHomeworkPanel>())
        {
            var topLeft = panel.TranslatePoint(new Point(0, 0), this);
            if (topLeft is null)
                continue;

            placements.Add((panel, topLeft.Value.Y, topLeft.Value.Y + panel.Bounds.Height));
        }

        if (placements.Count == 0)
            return;

        var sourceIndex = placements.FindIndex(x => ReferenceEquals(x.Panel, _dragSourceSubject));
        var fullSlot = placements.Count;
        for (var i = 0; i < placements.Count; i++)
        {
            var center = (placements[i].Top + placements[i].Bottom) / 2;
            if (point.Y < center)
            {
                fullSlot = i;
                break;
            }
        }

        var slot = sourceIndex >= 0 && fullSlot > sourceIndex
            ? fullSlot - 1
            : fullSlot;

        var lineY = fullSlot <= 0
            ? placements[0].Top - 12
            : fullSlot >= placements.Count
                ? placements[^1].Bottom + 12
                : (placements[fullSlot - 1].Bottom + placements[fullSlot].Top) / 2;

        _dropTargetColumn = targetColumn;
        _dropTargetSlot = slot;
        _hasDropTarget = true;
        ShowDropIndicator(columnTopLeft.Value.X, lineY, columns.Bounds.Width);
    }

    private void ShowDropIndicator(double x, double y, double width)
    {
        if (_dropIndicator is null || width <= 0)
            return;

        _dropIndicator.Width = width;
        _dropIndicator.IsVisible = true;
        Canvas.SetLeft(_dropIndicator, Math.Max(0, x));
        Canvas.SetTop(_dropIndicator, Math.Max(0, y - 1.5));
    }

    private void HideDropIndicator()
    {
        if (_dropIndicator is not null)
            _dropIndicator.IsVisible = false;
    }

    private void ApplyHomeworkItemDrop(HomeworkItemPanel sourceItem, int targetIndex)
    {
        try
        {
            var homework = StorageService.Load(Date) ?? new Homework(Date, []);
            if (homework.Frozen)
                return;

            if (!homework.MoveHomeworkItem(sourceItem.HomeworkItem.Id, targetIndex))
                return;

            StorageService.Save(homework);
            DisplayHomework(homework);
            _logger.Information("拖拽调整作业位置，科目: {Subject}，目标位置: {Index}",
                sourceItem.HomeworkItem.Subject, targetIndex);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "拖拽调整作业位置失败");
        }
    }

    private void ApplySubjectDrop(SubjectHomeworkPanel sourceSubject, int targetColumn, int targetSlot)
    {
        try
        {
            var homework = StorageService.Load(Date) ?? new Homework(Date, []);
            if (homework.Frozen)
                return;

            var subjects = homework.GetSubjectsInOrder();
            if (subjects.IndexOf(sourceSubject.Subject) < 0)
                return;

            var columnCount = HomeworkColumnCount;
            targetColumn = Math.Clamp(targetColumn, 0, columnCount - 1);

            // 以拖拽完成后的科目数为准计算每列容量，保证科目最终落在目标列的目标位置。
            var columnSizes = GetColumnSizes(subjects.Count, columnCount);
            var columnSize = columnSizes[targetColumn];
            var slot = Math.Clamp(targetSlot, 0, Math.Max(0, columnSize - 1));
            var targetIndex = GetColumnStartIndex(columnSizes, targetColumn) + slot;
            targetIndex = Math.Clamp(targetIndex, 0, subjects.Count - 1);

            if (!homework.MoveSubject(sourceSubject.Subject, targetIndex))
                return;

            StorageService.Save(homework);
            DisplayHomework(homework);
            _logger.Information("拖拽调整科目位置，科目: {Subject}，目标列: {Column}，目标位置: {Index}",
                sourceSubject.Subject, targetColumn, slot);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "拖拽调整科目位置失败");
        }
    }

    private void EndDrag(IPointer? pointer)
    {
        _dragAutoScrollTimer.Stop();
        _dragAutoScrollStopwatch.Reset();
        _isDragging = false;
        _isPointerTracking = false;
        _dragPointer = null;

        pointer?.Capture(null);

        if (_dragSourceControl is not null)
            _dragSourceControl.Opacity = 1;

        if (_dragGhost is not null)
        {
            DragOverlay.Children.Remove(_dragGhost);
            _dragGhost = null;
        }

        if (_dropIndicator is not null)
        {
            DragOverlay.Children.Remove(_dropIndicator);
            _dropIndicator = null;
        }

        _dragSourceControl = null;
        _dragSourceSubject = null;
        _dragSourceItem = null;
        _dragCandidateKind = DragSourceKind.None;
        _dragCandidateSubject = null;
        _dragCandidateItem = null;
        _hasDropTarget = false;

        // 恢复拖拽前正在运行的普通自动滚动。
        if (_isAutoScrollEnabled)
        {
            _autoScrollStopwatch.Restart();
            _autoScrollTimer.Start();
        }
    }

    private void CancelDrag()
    {
        if (!_isDragging && !_isPointerTracking)
            return;

        var pointer = _dragPointer;
        EndDrag(pointer);
    }

    private void DragAutoScrollTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_isDragging)
        {
            _dragAutoScrollTimer.Stop();
            return;
        }

        var elapsed = _dragAutoScrollStopwatch.Elapsed;
        _dragAutoScrollStopwatch.Restart();

        var viewportTopLeft = HomeworkScrollViewer.TranslatePoint(new Point(0, 0), this);
        if (viewportTopLeft is null)
            return;

        var viewportTop = viewportTopLeft.Value.Y;
        var viewportBottom = viewportTop + HomeworkScrollViewer.Bounds.Height;
        var direction = 0;
        if (_dragLastPoint.Y < viewportTop + DragAutoScrollEdge)
            direction = -1;
        else if (_dragLastPoint.Y > viewportBottom - DragAutoScrollEdge)
            direction = 1;

        if (direction == 0)
            return;

        var scrollableHeight = HomeworkScrollViewer.Extent.Height - HomeworkScrollViewer.Viewport.Height;
        if (scrollableHeight <= 0)
            return;

        var newOffset = Math.Clamp(
            HomeworkScrollViewer.Offset.Y + direction * DragAutoScrollSpeed * elapsed.TotalSeconds,
            0, scrollableHeight);

        if (Math.Abs(newOffset - HomeworkScrollViewer.Offset.Y) < 0.01)
            return;

        HomeworkScrollViewer.Offset = new Vector(HomeworkScrollViewer.Offset.X, newOffset);
        UpdateDropTarget(_dragLastPoint);
    }

    private static T? FindVisualAncestor<T>(Visual? visual) where T : Visual
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is T target)
                return target;
        }

        return null;
    }

    private static bool IsDescendantOf(Visual? visual, Visual ancestor)
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    #endregion

    private void StartAutoScroll()
    {
        // 启用时总是从顶部开始向下滚动
        _autoScrollDirection = 1;
        HomeworkScrollViewer.Offset = new Vector(HomeworkScrollViewer.Offset.X, 0);
        _autoScrollStopwatch.Restart();
        _autoScrollTimer.Start();
        _logger.Debug("自动滚动已启用");
    }

    private void StopAutoScroll()
    {
        _autoScrollTimer.Stop();
        _autoScrollStopwatch.Reset();
        _logger.Debug("自动滚动已停用");
    }

    private void AutoScrollTimer_OnTick(object? sender, EventArgs e)
    {
        var elapsed = _autoScrollStopwatch.Elapsed;
        _autoScrollStopwatch.Restart();

        var scrollableHeight = HomeworkScrollViewer.Extent.Height - HomeworkScrollViewer.Viewport.Height;
        if (scrollableHeight <= 0)
            return;

        var offset = HomeworkScrollViewer.Offset.Y +
                     _autoScrollDirection * AutoScrollSpeed * elapsed.TotalSeconds;

        // 到达底部或顶部后反向，形成上下往复的滚动
        if (offset >= scrollableHeight)
        {
            offset = scrollableHeight;
            _autoScrollDirection = -1;
        }
        else if (offset <= 0)
        {
            offset = 0;
            _autoScrollDirection = 1;
        }

        HomeworkScrollViewer.Offset = new Vector(HomeworkScrollViewer.Offset.X, offset);
    }

    private void OnEditRequested(HomeworkItem item)
    {
        _logger.Debug("作业项请求编辑，ID: {Id}", item.Id);
        EditRequested?.Invoke(item);
    }
}