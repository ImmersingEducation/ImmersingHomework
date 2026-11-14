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
using ImmersingHomework.Enums;
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
    /// 自动滚动的速度（像素/秒），由主界面设置中的“自动滚动速度”决定。
    /// </summary>
    private static double AutoScrollSpeed =>
        Math.Clamp(AppSettings.Instance.MainViewAutoScrollSpeed.Value, 10, 300);

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

    #region 作业内容入场动画

    /// <summary>
    /// 入场动画中横向位移与面板宽度的比例，以及允许的取值范围。
    /// </summary>
    private const double EntranceHorizontalOffsetRatio = 0.32;
    private const double EntranceMinHorizontalOffset = 220;
    private const double EntranceMaxHorizontalOffset = 460;

    /// <summary>
    /// 入场动画中纵向位移与面板高度的比例，以及允许的取值范围。
    /// 纵向位移会在动画前半段完成，之后内容只向左移动。
    /// </summary>
    private const double EntranceVerticalOffsetRatio = 0.22;
    private const double EntranceMinVerticalOffset = 90;
    private const double EntranceMaxVerticalOffset = 180;

    /// <summary>
    /// 内容从起点移动到最终位置所需的时间。同列的所有内容共用同一时长。
    /// </summary>
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(720);

    /// <summary>
    /// 相邻列之间的入场延迟。同一列的所有科目与卡片共用一个延迟，
    /// 保证同列动画同时开始、同时结束。
    /// </summary>
    private static readonly TimeSpan ColumnEntranceStagger = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan ColumnEntranceMaxStagger = TimeSpan.FromMilliseconds(280);

    /// <summary>
    /// 整列内容淡入所需的时间。
    /// </summary>
    private static readonly TimeSpan EntranceFadeDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// 入场动画的刷新间隔，按帧更新以保证滑动平滑。
    /// </summary>
    private static readonly TimeSpan EntranceFrameInterval = TimeSpan.FromMilliseconds(16);

    private sealed class EntranceState
    {
        public required SubjectHomeworkPanel Panel { get; init; }
        public required TranslateTransform Transform { get; init; }
        public required double StartX { get; init; }
        public required double StartY { get; init; }
        public required TimeSpan Delay { get; init; }
    }

    private readonly List<EntranceState> _entranceStates = [];

    private readonly DispatcherTimer _entranceTimer = new(DispatcherPriority.Render)
    {
        Interval = EntranceFrameInterval
    };

    private readonly Stopwatch _entranceStopwatch = new();

    /// <summary>
    /// 下一次内容替换完成后是否播放入场动画。打开主界面和切换日期时置为 true，
    /// 编辑、拖拽等普通刷新不会触发，避免打断用户操作。
    /// </summary>
    private bool _animateNextContent;

    #endregion

    #region 作业项变化动画

    /// <summary>
    /// 删除或编辑后，保留内容从旧位置移动到新位置所需的时间。
    /// </summary>
    private static readonly TimeSpan ChangeReflowDuration = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 正向播放时，新增或编辑后的新卡片滑入所需的时间。
    /// 反向播放时，该时长用于撤销新增的旧卡片滑出。
    /// </summary>
    private static readonly TimeSpan ChangeEnterDuration = TimeSpan.FromMilliseconds(360);

    /// <summary>
    /// 正向播放时，删除或编辑后的旧卡片滑出所需的时间。
    /// 反向播放时，该时长用于恢复被删除的卡片滑入。
    /// </summary>
    private static readonly TimeSpan ChangeExitDuration = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// 作业项变化动画的刷新间隔。
    /// </summary>
    private static readonly TimeSpan ChangeFrameInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// 卡片滑入/滑出的横向距离范围。
    /// </summary>
    private const double ChangeMinSlideOffset = 260;
    private const double ChangeMaxSlideOffset = 640;

    private enum ChangeEasing
    {
        OutCubic,
        InCubic
    }

    /// <summary>
    /// 一条作业项变化动画：控件从 (StartX, StartY) 移动到 (EndX, EndY)，
    /// 同时把透明度从 StartOpacity 渐变到 EndOpacity。
    /// </summary>
    private sealed class ChangeAnimationState
    {
        public required Control Control { get; init; }
        public required TranslateTransform Transform { get; init; }
        public required double StartX { get; init; }
        public required double StartY { get; init; }
        public required double EndX { get; init; }
        public required double EndY { get; init; }
        public required double StartOpacity { get; init; }
        public required double EndOpacity { get; init; }
        public required TimeSpan Duration { get; init; }
        public required TimeSpan Delay { get; init; }
        public required ChangeEasing Easing { get; init; }

        /// <summary>
        /// 动画结束后是否从拖拽浮层移除。旧作业卡片的滑出动画使用该标记。
        /// </summary>
        public bool RemoveFromOverlay { get; init; }
    }

    /// <summary>
    /// 内容替换前记录的旧布局。保留的科目和作业卡片通过旧、新位置差
    /// 计算 FLIP 位移，被删除或替换的卡片则单独复用到浮层中播放滑出动画。
    /// </summary>
    private sealed class HomeworkLayoutSnapshot
    {
        public Dictionary<string, Point> SubjectPositions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Size> SubjectSizes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, SubjectHomeworkPanel> SubjectPanels { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, Point> ItemPositions { get; } = new();
        public Dictionary<Guid, Size> ItemSizes { get; } = new();
        public Dictionary<Guid, HomeworkItemPanel> ItemPanels { get; } = new();
        public Dictionary<Guid, HomeworkItem> Items { get; } = new();
    }

    /// <summary>
    /// 一次内容切换中需要单独播放动画的作业项。
    /// <see cref="DiffFromDisplayedContent"/> 为 true 时，会在新内容完成布局后
    /// 自动与替换前的内容比较并填充三个集合。
    /// </summary>
    private sealed class HomeworkTransition
    {
        /// <summary>
        /// 目标内容是否早于当前显示内容。为 true 时新增从左侧进入、删除向右侧退出，
        /// 用于表现时光倒流。
        /// </summary>
        public required bool Reverse { get; init; }

        public bool DiffFromDisplayedContent { get; init; }

        public HashSet<Guid> AddedItemIds { get; } = [];
        public HashSet<Guid> RemovedItemIds { get; } = [];
        public HashSet<Guid> EditedItemIds { get; } = [];
    }

    private readonly List<ChangeAnimationState> _changeAnimationStates = [];

    private readonly DispatcherTimer _changeAnimationTimer = new(DispatcherPriority.Render)
    {
        Interval = ChangeFrameInterval
    };

    private readonly Stopwatch _changeAnimationStopwatch = new();

    #endregion

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

    /// <summary>
    /// 作业内容滚动位置发生变化。主窗口用它同步高级材质背景的镂空区域。
    /// </summary>
    public event EventHandler? ScrollOffsetChanged;

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
        _entranceTimer.Tick += EntranceTimer_OnTick;
        _changeAnimationTimer.Tick += ChangeAnimationTimer_OnTick;
        HomeworkScrollViewer.ScrollChanged += (_, _) => ScrollOffsetChanged?.Invoke(this, EventArgs.Empty);
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;

        // 使用隧道事件并启用 handledEventsToo，确保作业卡片自身处理点击后
        // 拖拽模式仍能从卡片和科目标题开始拖拽。
        AddHandler(PointerPressedEvent, HomeworkPanel_OnPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, HomeworkPanel_OnPointerMoved, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, HomeworkPanel_OnPointerReleased, RoutingStrategies.Tunnel, true);
        AddHandler(PointerCaptureLostEvent, HomeworkPanel_OnPointerCaptureLost, RoutingStrategies.Direct);
        _logger.Information("HomeworkPanel 初始化完成");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DateProperty)
        {
            _logger.Debug("日期改变: {Date}", Date);
            // 切换日期后，等新内容替换完成时播放一次入场动画。
            _animateNextContent = true;
            DateChanged?.Invoke(Date);
            // 依赖服务尚未注入时（如 XAML 预览）跳过加载，待 Initialize 注入后再补
            if (_storageService is not null)
                _ = RefreshAsync(Date);
        }
        else if (change.Property == IsFrozenProperty)
        {
            ApplyFrozenStateToSubjects();
            FrozenChanged?.Invoke(IsFrozen);
        }
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
        DisplayHomework(homework, HomeworkChangeKind.None, null);
    }

    /// <summary>
    /// 展示作业内容，并按本次变化类型播放对应的卡片动画。
    /// </summary>
    /// <param name="homework">要展示的完整作业。</param>
    /// <param name="changeKind">本次内容变化的类型。</param>
    /// <param name="changedItemId">发生变化的作业项 ID。新增、编辑或删除时传入。</param>
    public void DisplayHomework(Homework homework, HomeworkChangeKind changeKind, Guid? changedItemId)
    {
        _logger.Debug("直接展示作业内容，日期: {Date}，变化类型: {ChangeKind}",
            homework.Date, changeKind);
        // 直接展示会覆盖异步刷新的结果，使所有尚未完成的刷新失效。
        _contentVersion++;

        IsFrozen = homework.Frozen;

        HomeworkTransition? transition = null;
        if (changeKind != HomeworkChangeKind.None && changedItemId is { } itemId)
        {
            transition = new HomeworkTransition { Reverse = false };
            switch (changeKind)
            {
                case HomeworkChangeKind.Added:
                    transition.AddedItemIds.Add(itemId);
                    break;
                case HomeworkChangeKind.Edited:
                    transition.EditedItemIds.Add(itemId);
                    break;
                case HomeworkChangeKind.Deleted:
                    transition.RemovedItemIds.Add(itemId);
                    break;
            }
        }

        var (subjectPanels, hasHomework) = BuildHomeworkPanels(homework);
        ReplaceHomeworkContent(subjectPanels, hasHomework, transition);
        _logger.Debug("作业内容展示完成");
    }

    /// <summary>
    /// 展示作业内容，并自动比较当前内容与新内容的差异，为新增、删除和编辑的
    /// 作业项播放滑入/滑出动画。
    /// </summary>
    /// <param name="homework">要展示的完整作业。</param>
    /// <param name="reverse">
    /// 目标内容是否早于当前显示内容。为 true 时动画方向反转：新增项从左侧滑入，
    /// 删除项向右侧滑出，编辑项也按相反方向交叉切换，用于表现回到过去的快照。
    /// </param>
    public void DisplayHomeworkWithDiff(Homework homework, bool reverse)
    {
        _logger.Debug("按差异展示作业内容，日期: {Date}，反向: {Reverse}",
            homework.Date, reverse);
        // 直接展示会覆盖异步刷新的结果，使所有尚未完成的刷新失效。
        _contentVersion++;

        IsFrozen = homework.Frozen;

        var transition = new HomeworkTransition
        {
            Reverse = reverse,
            DiffFromDisplayedContent = true
        };

        var (subjectPanels, hasHomework) = BuildHomeworkPanels(homework);
        ReplaceHomeworkContent(subjectPanels, hasHomework, transition);
        _logger.Debug("作业内容展示完成");
    }

    public void Refresh()
    {
        _ = RefreshAsync(Date);
    }

    /// <summary>
    /// 重新加载当前日期的作业。
    /// </summary>
    /// <param name="animateCards">
    /// 新内容替换完成后是否播放入场动画。打开主界面时传入 <see langword="true"/>，
    /// 编辑、拖拽等普通刷新保持 <see langword="false"/>。
    /// </param>
    public void Refresh(bool animateCards)
    {
        if (animateCards)
            _animateNextContent = true;

        _ = RefreshAsync(Date);
    }

    public async Task RefreshAsync(DateOnly date)
    {
#if DEBUG
        var sw = Stopwatch.StartNew();
#endif
        _logger.Debug("刷新作业面板，日期: {Date}", date);
        var contentVersion = ++_contentVersion;

        // 先读取数据，旧内容保持可见，避免清空后等待磁盘 I/O 造成空白闪烁。
        var homework = await StorageService.LoadAsync(date);

        // 加载期间如果直接展示了快照或开始了更新的刷新，当前结果已经过期。
        if (contentVersion != _contentVersion)
        {
            _logger.Debug("作业面板刷新已过期，丢弃结果，日期: {Date}", date);
            return;
        }

        // 只在文件不存在且是空作业时才创建文件，避免重复保存
        if (homework != null &&
            (homework.HomeworkItems == null || homework.HomeworkItems.Count == 0) &&
            !StorageService.Exists(date))
        {
            _logger.Debug("自动创建空作业文件，日期: {Date}", date);
            // 自动保存这个空白作业，创建对应的日期文件（异步执行，不阻塞UI）
            _ = Task.Run(async () =>
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

        IsFrozen = homework?.Frozen ?? false;

        var (subjectPanels, hasHomework) = BuildHomeworkPanels(homework);
        ReplaceHomeworkContent(subjectPanels, hasHomework);

        _logger.Debug("作业面板刷新完成，有作业: {HasHomework}", hasHomework);
#if DEBUG
        sw.Stop();
        _logger.Debug("Refresh 方法执行耗时: {Elapsed}ms", sw.Elapsed.TotalMilliseconds);
#endif
    }

    /// <summary>
    /// 根据作业数据创建一组尚未挂到视觉树的科目面板。
    /// 先把新内容构建完成，再一次性替换旧内容，避免刷新过程中出现空白。
    /// </summary>
    private (List<SubjectHomeworkPanel> Panels, bool HasHomework) BuildHomeworkPanels(Homework? homework)
    {
        var subjectPanels = new List<SubjectHomeworkPanel>();
        if (homework?.HomeworkItems is not { Count: > 0 })
            return (subjectPanels, false);

        foreach (var subject in homework.GetSubjectsInOrder())
        {
            if (string.IsNullOrEmpty(subject))
                continue;

            var subjectItems = homework.GetHomeworkItemsBySubject(subject);
            if (subjectItems is not { Count: > 0 })
                continue;

            var subjectPanel = new SubjectHomeworkPanel();
            subjectPanel.SetData(subject, subjectItems);
            subjectPanel.EditRequested += OnEditRequested;
            subjectPanel.IsFrozen = IsFrozen;
            subjectPanel.IsDragModeEnabled = IsDragModeEnabled;
            subjectPanels.Add(subjectPanel);
        }

        return (subjectPanels, subjectPanels.Count > 0);
    }

    /// <summary>
    /// 用已经构建好的科目面板替换当前内容。旧内容在新内容就绪后才被移除，
    /// 因此不会出现“全部作业先消失、等待加载后再出现”的空白帧。
    /// </summary>
    private void ReplaceHomeworkContent(
        IReadOnlyList<SubjectHomeworkPanel> subjectPanels,
        bool hasHomework,
        HomeworkTransition? transition = null)
    {
        HomeworkLayoutSnapshot? layoutSnapshot = null;
        if (transition is not null && IsEffectivelyVisible)
        {
            // 先结束仍在播放的动画，保证测量到的是内容的最终布局位置。
            CancelEntranceAnimation();
            CancelChangeAnimation();
            layoutSnapshot = CaptureLayoutSnapshot();
        }

        ClearHomeworkGrid();
        _subjectPanels.AddRange(subjectPanels);
        ReflowSubjectPanels();

        HomeworkGrid.IsVisible = true;
        NoHomeworkText.IsVisible = !hasHomework;

        // 新内容已经挂到视觉树，先设置起始位移再开始动画，避免卡片在最终位置闪一帧。
        TryStartEntranceAnimation();

        if (layoutSnapshot is not null && transition is not null)
            TryStartTransitionAnimation(layoutSnapshot, transition);
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
        CancelEntranceAnimation();
        CancelChangeAnimation();
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

        // 旧内容的入场动画也一并停止并复位，避免计时器继续操作即将移除的卡片。
        CancelEntranceAnimation();

        // 作业项变化动画可能正在使用旧卡片作为浮层，清理后旧内容才能安全释放。
        CancelChangeAnimation();

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

        // 用户开始拖拽时立即结束入场动画，避免两个动画同时修改同一列内容的位置和透明度。
        CancelEntranceAnimation();

        // 拖拽浮层与作业项变化动画共用 DragOverlay，开始拖拽前先清理变化动画。
        CancelChangeAnimation();

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

    #region 作业内容入场动画

    /// <summary>
    /// 为当前内容播放入场动画。以每一列的最终位置为基准：
    /// 同列的所有科目文本和作业卡片共用一个延迟与时长，前半段同时向左、向上移动，
    /// 纵向到位后只继续向左，整体使用先快后慢的缓动，最后一帧精确落到最终位置。
    /// </summary>
    private void TryStartEntranceAnimation()
    {
        if (!_animateNextContent)
            return;

        _animateNextContent = false;

        // 主窗口尚未显示时不播放，等打开主界面时的刷新重新触发。
        if (!IsEffectivelyVisible)
            return;

        var subjectPanels = new List<(SubjectHomeworkPanel Panel, int Column)>();
        for (var column = 0; column < _homeworkColumns.Count; column++)
        {
            foreach (var panel in _homeworkColumns[column].Children.OfType<SubjectHomeworkPanel>())
                subjectPanels.Add((panel, column));
        }

        if (subjectPanels.Count == 0)
            return;

        var viewportWidth = HomeworkScrollViewer.Bounds.Width;
        var horizontalOffset = double.IsFinite(viewportWidth) && viewportWidth > 0
            ? Math.Clamp(viewportWidth * EntranceHorizontalOffsetRatio,
                EntranceMinHorizontalOffset, EntranceMaxHorizontalOffset)
            : EntranceMinHorizontalOffset;

        var viewportHeight = HomeworkScrollViewer.Bounds.Height;
        var verticalOffset = double.IsFinite(viewportHeight) && viewportHeight > 0
            ? Math.Clamp(viewportHeight * EntranceVerticalOffsetRatio,
                EntranceMinVerticalOffset, EntranceMaxVerticalOffset)
            : EntranceMinVerticalOffset;

        // 列数较多时压缩列间间隔，让最后一列也不会等太久。
        var maxColumn = subjectPanels.Max(x => x.Column);
        var staggerMilliseconds = maxColumn <= 0
            ? 0
            : Math.Min(ColumnEntranceStagger.TotalMilliseconds,
                ColumnEntranceMaxStagger.TotalMilliseconds / maxColumn);

        _entranceStates.Clear();
        foreach (var (panel, column) in subjectPanels)
        {
            var transform = panel.RenderTransform as TranslateTransform ?? new TranslateTransform();
            transform.X = horizontalOffset;
            transform.Y = verticalOffset;
            panel.RenderTransform = transform;
            panel.Opacity = 0;

            _entranceStates.Add(new EntranceState
            {
                Panel = panel,
                Transform = transform,
                StartX = horizontalOffset,
                StartY = verticalOffset,
                // 延迟只由列决定，同列科目与卡片因此严格同步。
                Delay = TimeSpan.FromMilliseconds(staggerMilliseconds * column)
            });
        }

        _entranceStopwatch.Restart();
        _entranceTimer.Start();
        _logger.Debug("开始作业内容入场动画，科目数: {Count}，列数: {Columns}",
            subjectPanels.Count, maxColumn + 1);
    }

    private void EntranceTimer_OnTick(object? sender, EventArgs e)
    {
        var elapsed = _entranceStopwatch.Elapsed;
        var allFinished = true;

        foreach (var state in _entranceStates)
        {
            var localElapsed = elapsed - state.Delay;
            if (localElapsed < TimeSpan.Zero)
            {
                allFinished = false;
                continue;
            }

            var progress = Math.Clamp(
                localElapsed.TotalMilliseconds / EntranceDuration.TotalMilliseconds, 0, 1);

            // 横向先快后慢；纵向在前半段完成，之后整列内容只剩横向移动。
            var horizontalProgress = 1 - Math.Pow(1 - progress, 2);
            var verticalProgress = 1 - Math.Pow(1 - Math.Min(progress / 0.5, 1), 3);

            state.Transform.X = state.StartX * (1 - horizontalProgress);
            state.Transform.Y = state.StartY * (1 - verticalProgress);

            var fadeProgress = Math.Clamp(
                localElapsed.TotalMilliseconds / EntranceFadeDuration.TotalMilliseconds, 0, 1);
            state.Panel.Opacity = fadeProgress;

            if (progress < 1)
            {
                allFinished = false;
                continue;
            }

            // 消除浮点误差，保证内容最终严格落在布局位置上。
            state.Transform.X = 0;
            state.Transform.Y = 0;
            state.Panel.Opacity = 1;
        }

        if (!allFinished)
            return;

        _entranceTimer.Stop();
        _entranceStopwatch.Reset();
        _entranceStates.Clear();
    }

    /// <summary>
    /// 停止尚未完成的入场动画，并把所有内容恢复到最终位置。
    /// </summary>
    private void CancelEntranceAnimation()
    {
        _entranceTimer.Stop();
        _entranceStopwatch.Reset();

        foreach (var state in _entranceStates)
        {
            state.Transform.X = 0;
            state.Transform.Y = 0;
            state.Panel.Opacity = 1;
        }

        _entranceStates.Clear();
    }

    #endregion

    #region 作业项变化动画实现

    /// <summary>
    /// 在内容替换前记录旧布局，供 FLIP 位移动画、差异比较和旧卡片滑出动画使用。
    /// </summary>
    private HomeworkLayoutSnapshot CaptureLayoutSnapshot()
    {
        var snapshot = new HomeworkLayoutSnapshot();

        foreach (var subjectPanel in _subjectPanels)
        {
            var subject = subjectPanel.Subject;
            if (!string.IsNullOrEmpty(subject))
            {
                snapshot.SubjectPanels[subject] = subjectPanel;

                var subjectPosition = subjectPanel.TranslatePoint(default, this);
                if (subjectPosition.HasValue)
                {
                    snapshot.SubjectPositions[subject] = subjectPosition.Value;
                    snapshot.SubjectSizes[subject] = subjectPanel.Bounds.Size;
                }
            }

            foreach (var itemPanel in subjectPanel.ItemPanelHost.Children.OfType<HomeworkItemPanel>())
            {
                if (itemPanel.HomeworkItem is not { } item)
                    continue;

                snapshot.ItemPanels[item.Id] = itemPanel;
                snapshot.Items[item.Id] = item;

                var itemPosition = itemPanel.TranslatePoint(default, this);
                if (itemPosition.HasValue)
                {
                    snapshot.ItemPositions[item.Id] = itemPosition.Value;
                    snapshot.ItemSizes[item.Id] = itemPanel.Bounds.Size;
                }
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 新内容完成布局后，为作业项差异和发生位移的旧内容建立动画：
    /// 新增/编辑的卡片从入场方向滑入，删除/编辑时的旧卡片向出场方向滑出，
    /// 其余保留的科目和卡片按旧、新位置差移动到新位置。
    /// </summary>
    private void TryStartTransitionAnimation(
        HomeworkLayoutSnapshot snapshot,
        HomeworkTransition transition)
    {
        if (!IsEffectivelyVisible)
            return;

        // 变化动画和整页入场动画不能同时修改同一批控件的位移与透明度。
        CancelEntranceAnimation();
        _animateNextContent = false;

        // 先执行一次布局，才能读取新内容的位置并计算 FLIP 偏移。
        UpdateLayout();

        var newSubjectPositions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var newSubjectPanels = new Dictionary<string, SubjectHomeworkPanel>(StringComparer.Ordinal);
        var newItemPositions = new Dictionary<Guid, Point>();
        var newItemPanels = new Dictionary<Guid, HomeworkItemPanel>();
        var newItems = new Dictionary<Guid, HomeworkItem>();

        foreach (var subjectPanel in _subjectPanels)
        {
            var subject = subjectPanel.Subject;
            if (!string.IsNullOrEmpty(subject))
            {
                newSubjectPanels[subject] = subjectPanel;

                var subjectPosition = subjectPanel.TranslatePoint(default, this);
                if (subjectPosition.HasValue)
                    newSubjectPositions[subject] = subjectPosition.Value;
            }

            foreach (var itemPanel in subjectPanel.ItemPanelHost.Children.OfType<HomeworkItemPanel>())
            {
                if (itemPanel.HomeworkItem is not { } item)
                    continue;

                newItemPanels[item.Id] = itemPanel;
                newItems[item.Id] = item;

                var itemPosition = itemPanel.TranslatePoint(default, this);
                if (itemPosition.HasValue)
                    newItemPositions[item.Id] = itemPosition.Value;
            }
        }

        if (transition.DiffFromDisplayedContent)
        {
            foreach (var (itemId, newItem) in newItems)
            {
                if (!snapshot.Items.TryGetValue(itemId, out var oldItem))
                    transition.AddedItemIds.Add(itemId);
                else if (!AreHomeworkItemsEquivalent(oldItem, newItem))
                    transition.EditedItemIds.Add(itemId);
            }

            foreach (var itemId in snapshot.Items.Keys)
            {
                if (!newItems.ContainsKey(itemId))
                    transition.RemovedItemIds.Add(itemId);
            }
        }

        var subjectDeltas = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        // 先给保留下来的科目面板设置从旧位置到新位置的位移补偿。
        foreach (var (subject, subjectPanel) in newSubjectPanels)
        {
            if (!snapshot.SubjectPositions.TryGetValue(subject, out var oldPosition) ||
                !newSubjectPositions.TryGetValue(subject, out var newPosition))
                continue;

            var deltaX = oldPosition.X - newPosition.X;
            var deltaY = oldPosition.Y - newPosition.Y;
            subjectDeltas[subject] = (deltaX, deltaY);

            if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5)
                continue;

            AddChangeMoveAnimation(subjectPanel, deltaX, deltaY, ChangeReflowDuration);
        }

        // 新科目整体从入场方向滑入，科目标题和新卡片保持同步。
        foreach (var (subject, subjectPanel) in newSubjectPanels)
        {
            if (snapshot.SubjectPanels.ContainsKey(subject) ||
                !newSubjectPositions.TryGetValue(subject, out var subjectPosition))
                continue;

            AddChangeEnterAnimation(subjectPanel, subjectPosition, transition.Reverse);
        }

        // 消失的旧科目整体向出场方向滑出，不再单独处理其中的旧卡片。
        foreach (var (subject, oldSubjectPanel) in snapshot.SubjectPanels)
        {
            if (newSubjectPanels.ContainsKey(subject) ||
                !snapshot.SubjectPositions.TryGetValue(subject, out var oldPosition))
                continue;

            var oldSize = snapshot.SubjectSizes.TryGetValue(subject, out var subjectSize)
                ? subjectSize
                : oldSubjectPanel.Bounds.Size;
            AddChangeExitAnimation(oldSubjectPanel, oldPosition, oldSize, transition.Reverse);
        }

        // 已存在科目中的删除项和被替换的旧版本滑出。
        var exitingItemIds = new HashSet<Guid>(transition.RemovedItemIds);
        exitingItemIds.UnionWith(transition.EditedItemIds);
        foreach (var itemId in exitingItemIds)
        {
            if (!snapshot.ItemPanels.TryGetValue(itemId, out var oldItemPanel) ||
                !snapshot.ItemPositions.TryGetValue(itemId, out var oldPosition))
                continue;

            var oldSubject = snapshot.Items.TryGetValue(itemId, out var oldItem)
                ? oldItem.Subject
                : null;

            // 整个旧科目已经在上面整体滑出，不再重复处理其内部作业。
            if (string.IsNullOrEmpty(oldSubject) || !newSubjectPanels.ContainsKey(oldSubject))
                continue;

            var oldSize = snapshot.ItemSizes.TryGetValue(itemId, out var itemSize)
                ? itemSize
                : oldItemPanel.Bounds.Size;
            AddChangeExitAnimation(oldItemPanel, oldPosition, oldSize, transition.Reverse);
        }

        // 已存在科目中的新增项和编辑后的新版本滑入。
        foreach (var (itemId, itemPanel) in newItemPanels)
        {
            if (!transition.AddedItemIds.Contains(itemId) &&
                !transition.EditedItemIds.Contains(itemId))
                continue;

            var subject = itemPanel.HomeworkItem?.Subject;
            // 新科目整体进入时，卡片跟随父面板移动，不再单独设置位移。
            if (string.IsNullOrEmpty(subject) || !snapshot.SubjectPanels.ContainsKey(subject))
                continue;

            if (newItemPositions.TryGetValue(itemId, out var itemPosition))
                AddChangeEnterAnimation(itemPanel, itemPosition, transition.Reverse);
        }

        // 其余保留作业项按父科目位移之外的距离做 FLIP。
        foreach (var (itemId, itemPanel) in newItemPanels)
        {
            if (transition.AddedItemIds.Contains(itemId) ||
                transition.EditedItemIds.Contains(itemId))
                continue;

            var subject = itemPanel.HomeworkItem?.Subject;
            // 新科目整体进入时，其内部卡片不需要单独的位移补偿。
            if (string.IsNullOrEmpty(subject) ||
                !snapshot.SubjectPanels.ContainsKey(subject) ||
                !snapshot.ItemPositions.TryGetValue(itemId, out var oldPosition) ||
                !newItemPositions.TryGetValue(itemId, out var newPosition))
                continue;

            subjectDeltas.TryGetValue(subject, out var subjectDelta);
            var deltaX = oldPosition.X - newPosition.X - subjectDelta.X;
            var deltaY = oldPosition.Y - newPosition.Y - subjectDelta.Y;
            if (Math.Abs(deltaX) < 0.5 && Math.Abs(deltaY) < 0.5)
                continue;

            AddChangeMoveAnimation(itemPanel, deltaX, deltaY, ChangeReflowDuration);
        }

        if (_changeAnimationStates.Count == 0)
            return;

        _logger.Debug("开始作业项过渡动画，反向: {Reverse}，新增: {Added}，删除: {Removed}，编辑: {Edited}",
            transition.Reverse, transition.AddedItemIds.Count,
            transition.RemovedItemIds.Count, transition.EditedItemIds.Count);

        _changeAnimationStopwatch.Restart();
        _changeAnimationTimer.Start();
    }

    private void AddChangeEnterAnimation(Control control, Point finalPosition, bool reverse)
    {
        var width = control.Bounds.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = control.DesiredSize.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = 320;

        var offset = Math.Clamp(width + 120, ChangeMinSlideOffset, ChangeMaxSlideOffset);

        // 窗口较宽时尽量让控件从内容区边缘之外开始，避免直接出现在可视区域中间。
        var viewportWidth = HomeworkScrollViewer.Bounds.Width;
        if (double.IsFinite(viewportWidth) && viewportWidth > 0)
        {
            var viewportOffset = reverse
                ? finalPosition.X + 24
                : viewportWidth - finalPosition.X + 24;
            if (viewportOffset > offset)
                offset = Math.Min(viewportOffset, ChangeMaxSlideOffset);
        }

        var startX = reverse ? -offset : offset;
        var transform = EnsureTranslateTransform(control);
        transform.X = startX;
        transform.Y = 0;
        control.Opacity = 0;

        _changeAnimationStates.Add(new ChangeAnimationState
        {
            Control = control,
            Transform = transform,
            StartX = startX,
            StartY = 0,
            EndX = 0,
            EndY = 0,
            StartOpacity = 0,
            EndOpacity = 1,
            // 反向播放时，入场对应一次被撤销的删除，沿用删除动画的时长。
            Duration = reverse ? ChangeExitDuration : ChangeEnterDuration,
            Delay = TimeSpan.Zero,
            Easing = ChangeEasing.OutCubic
        });
    }

    private void AddChangeMoveAnimation(Control control, double startX, double startY, TimeSpan duration)
    {
        var transform = EnsureTranslateTransform(control);
        transform.X = startX;
        transform.Y = startY;

        _changeAnimationStates.Add(new ChangeAnimationState
        {
            Control = control,
            Transform = transform,
            StartX = startX,
            StartY = startY,
            EndX = 0,
            EndY = 0,
            StartOpacity = 1,
            EndOpacity = 1,
            Duration = duration,
            Delay = TimeSpan.Zero,
            Easing = ChangeEasing.OutCubic
        });
    }

    /// <summary>
    /// 把删除或被替换的旧控件移到浮层中，从旧位置向出场方向滑出。
    /// 正向播放时向左滑出，反向播放（时光倒流）时向右滑出。
    /// </summary>
    private void AddChangeExitAnimation(Control control, Point oldPosition, Size oldSize, bool reverse)
    {
        var width = oldSize.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = control.Bounds.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = 320;

        // 旧内容已经被清空，这里复用原控件以保持和原卡片完全一致的外观。
        if (control.Parent is Panel oldParent)
            oldParent.Children.Remove(control);

        control.Width = width;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        control.Opacity = 1;
        control.IsHitTestVisible = false;

        var transform = new TranslateTransform();
        control.RenderTransform = transform;

        Canvas.SetLeft(control, oldPosition.X);
        Canvas.SetTop(control, oldPosition.Y);
        DragOverlay.Children.Add(control);

        var viewportWidth = HomeworkScrollViewer.Bounds.Width;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
            viewportWidth = Bounds.Width;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
            viewportWidth = ChangeMaxSlideOffset;

        // 尽量让旧卡片完整滑出视口，同时保留淡出以覆盖距离过长的情况。
        var exitDistance = reverse
            ? viewportWidth - oldPosition.X + 24
            : oldPosition.X + width + 24;
        exitDistance = Math.Clamp(exitDistance, ChangeMinSlideOffset, ChangeMaxSlideOffset);
        var exitOffset = reverse ? exitDistance : -exitDistance;

        _changeAnimationStates.Add(new ChangeAnimationState
        {
            Control = control,
            Transform = transform,
            StartX = 0,
            StartY = 0,
            EndX = exitOffset,
            EndY = 0,
            StartOpacity = 1,
            EndOpacity = 0,
            // 反向播放时，出场对应一次被撤销的新增，沿用新增动画的时长。
            Duration = reverse ? ChangeEnterDuration : ChangeExitDuration,
            Delay = TimeSpan.Zero,
            Easing = ChangeEasing.InCubic,
            RemoveFromOverlay = true
        });
    }

    /// <summary>
    /// 判断两个作业项的内容是否完全一致。ID 相同但任意展示内容变化时，
    /// 时光机差异动画会按“编辑”处理。
    /// </summary>
    private static bool AreHomeworkItemsEquivalent(HomeworkItem left, HomeworkItem right)
    {
        if (!string.Equals(left.Subject, right.Subject, StringComparison.Ordinal) ||
            !string.Equals(left.Content, right.Content, StringComparison.Ordinal) ||
            !string.Equals(left.TemplateName, right.TemplateName, StringComparison.Ordinal))
            return false;

        var leftParameters = left.TemplateParameters ?? [];
        var rightParameters = right.TemplateParameters ?? [];
        if (!leftParameters.SequenceEqual(rightParameters, StringComparer.Ordinal))
            return false;

        var leftTags = left.Tags ?? [];
        var rightTags = right.Tags ?? [];
        if (leftTags.Count != rightTags.Count)
            return false;

        for (var i = 0; i < leftTags.Count; i++)
        {
            var leftTag = leftTags[i];
            var rightTag = rightTags[i];
            if (!string.Equals(leftTag.Name, rightTag.Name, StringComparison.Ordinal) ||
                leftTag.Color.A != rightTag.Color.A ||
                leftTag.Color.R != rightTag.Color.R ||
                leftTag.Color.G != rightTag.Color.G ||
                leftTag.Color.B != rightTag.Color.B)
                return false;
        }

        return true;
    }

    private void ChangeAnimationTimer_OnTick(object? sender, EventArgs e)
    {
        var elapsed = _changeAnimationStopwatch.Elapsed;
        var allFinished = true;

        for (var i = _changeAnimationStates.Count - 1; i >= 0; i--)
        {
            var state = _changeAnimationStates[i];
            var localElapsed = elapsed - state.Delay;
            if (localElapsed < TimeSpan.Zero)
            {
                allFinished = false;
                continue;
            }

            var progress = Math.Clamp(
                localElapsed.TotalMilliseconds / state.Duration.TotalMilliseconds, 0, 1);
            var eased = ApplyChangeEasing(state.Easing, progress);

            state.Transform.X = state.StartX + (state.EndX - state.StartX) * eased;
            state.Transform.Y = state.StartY + (state.EndY - state.StartY) * eased;
            state.Control.Opacity = state.StartOpacity +
                                    (state.EndOpacity - state.StartOpacity) * eased;

            if (progress < 1)
            {
                allFinished = false;
                continue;
            }

            // 消除浮点误差，保证内容最终严格落在布局位置上。
            state.Transform.X = state.EndX;
            state.Transform.Y = state.EndY;
            state.Control.Opacity = state.EndOpacity;

            if (state.RemoveFromOverlay && DragOverlay.Children.Contains(state.Control))
                DragOverlay.Children.Remove(state.Control);

            _changeAnimationStates.RemoveAt(i);
        }

        if (!allFinished)
            return;

        _changeAnimationTimer.Stop();
        _changeAnimationStopwatch.Reset();
        _changeAnimationStates.Clear();
    }

    /// <summary>
    /// 停止尚未完成的变化动画，并把保留内容恢复到最终位置、清除滑出浮层。
    /// </summary>
    private void CancelChangeAnimation()
    {
        _changeAnimationTimer.Stop();
        _changeAnimationStopwatch.Reset();

        foreach (var state in _changeAnimationStates)
        {
            if (state.RemoveFromOverlay)
            {
                if (DragOverlay.Children.Contains(state.Control))
                    DragOverlay.Children.Remove(state.Control);
                continue;
            }

            state.Transform.X = 0;
            state.Transform.Y = 0;
            state.Control.Opacity = 1;
        }

        _changeAnimationStates.Clear();
    }

    private static double ApplyChangeEasing(ChangeEasing easing, double progress) =>
        easing switch
        {
            ChangeEasing.OutCubic => 1 - Math.Pow(1 - progress, 3),
            ChangeEasing.InCubic => progress * progress * progress,
            _ => progress
        };

    private static TranslateTransform EnsureTranslateTransform(Control control)
    {
        if (control.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        control.RenderTransform = transform;
        return transform;
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