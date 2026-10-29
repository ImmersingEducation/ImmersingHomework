using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Controls;
using ImmersingHomework.Enums;
using ImmersingHomework.Shared.Enums;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Shared.Services;
using ImmersingHomework.Services;
using Serilog;
using Timer = System.Timers.Timer;

namespace ImmersingHomework.Views;

public partial class MainWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<MainWindow>();
    private Timer _hitokotoTimer;
    private bool _isFrozen;
    private bool _isAutoScrollEnabled;
    private string? _lastHitokotoText;

    /// <summary>
    /// 主抽屉的状态
    /// </summary>
    private enum MainDrawerState
    {
        Closed,
        Opened
    }

    /// <summary>
    /// 主抽屉面板高度占可用高度的比例，0.5 即半模态
    /// </summary>
    private const double MainDrawerContentHeightRatio = 0.5;

    private MainDrawerState _mainDrawerState = MainDrawerState.Closed;
    private double _mainDrawerContainerHeight;
    private bool _mainDrawerLayoutInitialized;

    /// <summary>
    /// 时光机面板的状态
    /// </summary>
    private enum TimeMachineState
    {
        Closed,
        Opened
    }

    /// <summary>
    /// 时光机面板高度占可用高度的比例
    /// </summary>
    private const double TimeMachineContentHeightRatio = 0.28;

    /// <summary>
    /// 时光机面板的最小高度，保证标题、轮盘和操作按钮都能放下
    /// </summary>
    private const double TimeMachineMinHeight = 240;

    /// <summary>
    /// 时光机面板的最大高度
    /// </summary>
    private const double TimeMachineMaxHeight = 280;

    private TimeMachineState _timeMachineState = TimeMachineState.Closed;
    private double _timeMachineContainerHeight;
    private bool _timeMachineLayoutInitialized;
    private readonly TranslateTransform _timeMachinePanelTransform = new();
    private CancellationTokenSource? _timeMachinePanelAnimationCts;

    /// <summary>
    /// 时光机面板滑动时长。
    /// </summary>
    private static readonly TimeSpan TimeMachinePanelSlideDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// 连续滚动轮盘时，等待用户停下来后再加载并预览快照，避免每一帧都重建作业内容。
    /// </summary>
    private static readonly TimeSpan PreviewSelectionDebounce = TimeSpan.FromMilliseconds(80);

    private DispatcherTimer? _previewDebounceTimer;

    /// <summary>
    /// 正在进行的异步加载数量。多个加载重叠时，全部结束后才隐藏等待浮层。
    /// </summary>
    private int _homeworkLoadingCount;

    /// <summary>
    /// 轮盘松手后、预览加载真正开始前的等待状态。让进度环在松手时立即出现。
    /// </summary>
    private bool _previewSelectionLoading;

    /// <summary>
    /// 按文件路径缓存快照的异步加载任务。快照文件不会在生成后修改，
    /// 因此同一路径可以安全复用；替换/合并使用过的快照会从缓存移除。
    /// </summary>
    private readonly Dictionary<string, Task<Homework?>> _snapshotHomeworkCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 时光机预览切换时，作业内容区横向滑动的时长。
    /// </summary>
    private static readonly TimeSpan HomeworkContentSlideDuration = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// 作业内容区尚未完成布局时使用的滑动距离。
    /// </summary>
    private const double HomeworkContentSlideFallbackOffset = 960;

    /// <summary>
    /// 用于同时展示新旧两份作业内容的预览面板。平时隐藏，切换快照时从侧面滑入。
    /// </summary>
    private HomeworkPanel _homeworkPreviewPanel = null!;

    private TranslateTransform _homeworkContentTransform = new();
    private TranslateTransform _homeworkPreviewTransform = new();

    /// <summary>
    /// 当前主内容区正在预览的快照，以及它在时光机列表中的位置。
    /// </summary>
    private SnapshotInfo? _previewedSnapshot;
    private int _previewedSnapshotIndex = -1;

    /// <summary>
    /// 等待处理的预览目标。快速连续切换快照时只保留最新目标，避免动画互相覆盖。
    /// </summary>
    private SnapshotInfo? _pendingPreviewSnapshot;
    private bool _hasPendingPreviewSnapshot;
    private bool _isPreviewTransitionRunning;
    private CancellationTokenSource? _previewTransitionCts;

    /// <summary>
    /// 作业内容版本号。直接展示新内容会让正在播放的滑动动画失效，
    /// 防止旧动画结束后覆盖新内容。
    /// </summary>
    private int _homeworkContentVersion;

    /// <summary>
    /// 替换/合并正在改变主内容区时，暂时忽略关闭请求，避免动画结果覆盖恢复后的内容。
    /// </summary>
    private bool _isTimeMachineActionInProgress;

    /// <summary>
    /// 冻结状态下长按解冻所需的时间
    /// </summary>
    private static readonly TimeSpan FreezeHoldDuration = TimeSpan.FromMilliseconds(500);

    private readonly Stopwatch _freezeHoldStopwatch = new();
    private DispatcherTimer? _freezeHoldTimer;
    private bool _isFreezeHoldActive;
    private bool _suppressFreezeClick;
    private const string FrozenHintText = "作业已冻结，请解除冻结后编辑作业";
    private static readonly IBrush FrozenBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(211, 211, 211));
    
    private event Action<DateOnly> DateChanged;
    
    public event EventHandler? WindowMinimized;
    public event EventHandler? WindowActivated;
    public event EventHandler? WindowDeactivated;

    public DateOnly Date
    {
        get;
        set
        {
            field = value;
            DateChanged?.Invoke(field);
        }
    }

    private readonly IHomeworkStorageService _storageService;
    private readonly ISnapshotStorageService _snapshotStorageService;
    private readonly IHomeworkMergeService _homeworkMergeService;
    private readonly IHitokotoService _hitokotoService;
    private readonly IClassIslandService _classIslandService;
    private Bitmap? _clipboardBitmap;

    /// <summary>
    /// InfoBar 消息区域内最多同时显示的消息条数，超出时自动移除最早的一条
    /// </summary>
    private const int MaxInfoBarCount = 3;

    /// <summary>
    /// 消息未指定显示时长时的默认显示时长
    /// </summary>
    private static readonly TimeSpan DefaultInfoBarDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 消息浮层与下方按钮区之间的间距
    /// </summary>
    private const double InfoBarPanelSpacing = 8;

    /// <summary>
    /// 有消息时消息浮层的外边距，底部留出按钮区的高度和底部外边距使其不遮挡按钮
    /// </summary>
    private Avalonia.Thickness InfoBarPanelMargin =>
        new(0, 0, 0, ButtonBar.Bounds.Height + ButtonBar.Margin.Bottom + InfoBarPanelSpacing);

    /// <summary>
    /// InfoBar 入场动画的起始 Y 偏移，即从上方滑入的距离
    /// </summary>
    private const double InfoBarEnterOffset = -12;

    /// <summary>
    /// InfoBar 出场动画的 X 位移，即向右移出的距离
    /// </summary>
    private const double InfoBarExitOffset = 40;

    /// <summary>
    /// InfoBar 出场动画时长
    /// </summary>
    private static readonly TimeSpan InfoBarExitDuration = TimeSpan.FromMilliseconds(200);

    public MainWindow(
        IHomeworkStorageService storageService,
        ISnapshotStorageService snapshotStorageService,
        IHomeworkMergeService homeworkMergeService,
        IHitokotoService hitokotoService,
        IClassIslandService classIslandService)
    {
        _logger.Information("MainWindow 初始化开始");
        InitializeComponent();

        // 时光机面板通过 RenderTransform 滑动，避免动画 Canvas.Bottom/Height 触发布局。
        TimeMachinePanel.RenderTransform = _timeMachinePanelTransform;
        _previewDebounceTimer = new DispatcherTimer { Interval = PreviewSelectionDebounce };
        _previewDebounceTimer.Tick += PreviewDebounceTimer_OnTick;

        _storageService = storageService;
        _snapshotStorageService = snapshotStorageService;
        _homeworkMergeService = homeworkMergeService;
        _hitokotoService = hitokotoService;
        _classIslandService = classIslandService;

        // HomeworkPanel / HomeworkEditView 由 MainWindow.axaml 以 XAML 方式创建，
        // 无法使用构造函数注入，由这里显式注入
        HomeworkPanel.Initialize(storageService, classIslandService);
        HomeworkPanel.RenderTransform = _homeworkContentTransform;

        // 时光机预览面板不自动加载当天作业，只在切换到快照时由 DisplayHomework 填充。
        // 它和主面板叠放在 HomeworkContentHost 中，切换快照时一前一后横向滑动。
        _homeworkPreviewPanel = new HomeworkPanel(false)
        {
            IsVisible = false,
            IsHitTestVisible = false,
            RenderTransform = _homeworkPreviewTransform
        };
        _homeworkPreviewPanel.Initialize(storageService, classIslandService);
        HomeworkContentHost.Children.Add(_homeworkPreviewPanel);

        MainDrawerEditView.Initialize(classIslandService, storageService);

        MainDrawerEditView.Saved += OnMainDrawerHomeworkSaved;
        MainDrawerEditView.Deleted += OnMainDrawerHomeworkDeleted;

        UpdateTimeMachineSelection(null);

        WindowState = WindowState.FullScreen;
        
        DateChanged += UpdateDateText;
        DateChanged += (date) => HomeworkPanel.Date = date;
        // 日期切换后旧快照列表已不再适用，直接收起时光机。
        // 日期变化时不播放退出动画，并让旧日期的动画结果失效。
        DateChanged += _ =>
        {
            _snapshotHomeworkCache.Clear();
            _homeworkContentVersion++;
            CloseTimeMachine(restoreContent: true, animateRestore: false);
        };
        Date = DateOnly.FromDateTime(DateTime.Now);
        
        CalendarPopup.PlacementTarget = DateButton;

        // 打开状态下按 Esc 直接关闭主抽屉
        KeyDown += MainWindow_OnKeyDown;

        // Button 会在自己的 PointerPressed/Released 中把事件标记为已处理，
        // 因此需要 handledEventsToo 才能可靠地驱动冻结长按进度
        FreezeButton.AddHandler(PointerPressedEvent, FreezeButton_OnPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        FreezeButton.AddHandler(PointerReleasedEvent, FreezeButton_OnPointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        FreezeButton.AddHandler(PointerCaptureLostEvent, FreezeButton_OnPointerCaptureLost,
            RoutingStrategies.Direct);
        
        this.Activated += (s, e) => 
        {
            _logger.Debug("窗口激活");
            WindowActivated?.Invoke(this, EventArgs.Empty);
        };
        this.Deactivated += (s, e) => 
        {
            _logger.Debug("窗口失活");
            WindowDeactivated?.Invoke(this, EventArgs.Empty);
        };

        HomeworkPanel.FrozenChanged += ApplyFrozenState;
        HomeworkPanel.EditRequested += item => OpenMainDrawer(item);
        HomeworkPanel.Refresh();
        _logger.Information("MainWindow 初始化完成");
    }

    
    
    public void UpdateDateText(DateOnly date)
    {
        DateText.Text = $"{ Date.Month }月{ Date.Day }日";
        DateTextBlock.Text = $"{Date.Month}月{Date.Day}日";
    }

    /// <summary>
    /// 在主窗口右下角的 InfoBar 区域显示一条消息
    /// </summary>
    /// <param name="severity">消息级别</param>
    /// <param name="title">消息标题</param>
    /// <param name="message">消息内容，可为空</param>
    /// <param name="duration">
    /// 消息自动关闭前的停留时长，传入 <see cref="TimeSpan.Zero"/> 或负数表示不自动关闭
    /// </param>
    /// <param name="actionButton">可选的操作按钮内容，为空时不显示操作按钮</param>
    public void ShowInfoBar(FAInfoBarSeverity severity, string title, string? message = null,
        TimeSpan? duration = null, Control? actionButton = null)
    {
        var infoBar = new FAInfoBar
        {
            Severity = severity,
            Title = title,
            Message = message,
            IsClosable = true,
            IsOpen = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // 入场动画改 TranslateTransform.Y，出场动画改 TranslateTransform.X，
            // 需在此显式赋值，否则动画会因主题覆盖 RenderTransform 而失效
            RenderTransform = new TranslateTransform(0, InfoBarEnterOffset)
        };

        if (actionButton is not null)
            infoBar.ActionButton = actionButton;

        infoBar.Closed += (_, _) =>
        {
            InfoBarPanel.Children.Remove(infoBar);
            if (InfoBarPanel.Children.Count == 0)
                InfoBarPanel.Margin = default;
        };

        while (InfoBarPanel.Children.Count >= MaxInfoBarCount)
            InfoBarPanel.Children.RemoveAt(0);

        _logger.Debug("显示 InfoBar 消息: [{Severity}] {Title} {Message}", severity, title, message ?? string.Empty);
        InfoBarPanel.Margin = InfoBarPanelMargin;
        InfoBarPanel.Children.Add(infoBar);
        infoBar.IsOpen = true;

        var timeout = duration ?? DefaultInfoBarDuration;
        if (timeout <= TimeSpan.Zero)
            return;

        var timer = new DispatcherTimer { Interval = timeout };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // 已被移除的（如超出数量上限）无需再处理
            if (InfoBarPanel.Children.Contains(infoBar))
                FireAndForget(CloseInfoBarAsync(infoBar));
        };
        // 用户提前关闭时无需再等待
        infoBar.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    /// <summary>
    /// 播放向右移出的动画，动画结束后关闭并销毁该消息
    /// </summary>
    private async Task CloseInfoBarAsync(FAInfoBar infoBar)
    {
        var animation = new Animation
        {
            Duration = InfoBarExitDuration,
            Easing = new CubicEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Avalonia.Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.XProperty, 0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Avalonia.Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.XProperty, InfoBarExitOffset)
                    }
                }
            }
        };

        await animation.RunAsync(infoBar);
        // 动画结束后真正关闭，Closed 事件会将其从面板移除并释放
        if (InfoBarPanel.Children.Contains(infoBar))
            infoBar.IsOpen = false;
    }

    /// <summary>
    /// 启动一个无需等待的异步操作，并记录未处理的异常
    /// </summary>
    private void FireAndForget(Task task)
    {
        _ = task.ContinueWith(t => _logger.Error(t.Exception, "InfoBar 移出动画执行失败"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// 显示一条普通信息
    /// </summary>
    public void ShowInformation(string title, string? message = null,
        TimeSpan? duration = null, Control? actionButton = null) =>
        ShowInfoBar(FAInfoBarSeverity.Informational, title, message, duration, actionButton);

    /// <summary>
    /// 显示一条成功信息
    /// </summary>
    public void ShowSuccess(string title, string? message = null,
        TimeSpan? duration = null, Control? actionButton = null) =>
        ShowInfoBar(FAInfoBarSeverity.Success, title, message, duration, actionButton);

    /// <summary>
    /// 显示一条警告信息
    /// </summary>
    public void ShowWarning(string title, string? message = null,
        TimeSpan? duration = null, Control? actionButton = null) =>
        ShowInfoBar(FAInfoBarSeverity.Warning, title, message, duration, actionButton);

    /// <summary>
    /// 显示一条错误信息
    /// </summary>
    public void ShowError(string title, string? message = null,
        TimeSpan? duration = null, Control? actionButton = null) =>
        ShowInfoBar(FAInfoBarSeverity.Error, title, message, duration, actionButton);

    /// <summary>
    /// 关闭当前显示的所有 InfoBar 消息
    /// </summary>
    public void ClearInfoBars()
    {
        foreach (var infoBar in InfoBarPanel.Children.OfType<FAInfoBar>())
            FireAndForget(CloseInfoBarAsync(infoBar));
    }

    /// <summary>
    /// 截取过长的作业内容摘要，避免 InfoBar 消息过长
    /// </summary>
    private static string Summarize(string? content, int maxLength = 40) =>
        string.IsNullOrEmpty(content) ? string.Empty
            : content.Length <= maxLength ? content
            : $"{content[..maxLength]}…";

    private void DateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CalendarPopup.IsOpen = true;
    }

    private void Calendar_OnSelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        Date = Calendar.SelectedDate.HasValue
            ? DateOnly.FromDateTime(Calendar.SelectedDate.Value)
            : Date;
    }

    #region 主抽屉

    /// <summary>
    /// 主抽屉当前关联的作业；<see langword="null"/> 表示新增作业
    /// </summary>
    public HomeworkItem? MainDrawerHomeworkItem { get; private set; }

    /// <summary>
    /// 打开主抽屉。传入作业时抽屉内容绑定该作业，不传表示新增作业。
    /// </summary>
    public void OpenMainDrawer(HomeworkItem? homeworkItem = null)
    {
        CloseTimeMachine();

        MainDrawerHomeworkItem = homeworkItem;
        MainDrawerTitle.Text = homeworkItem is null ? "新增作业" : "编辑作业";
        // DataContext 供后续绑定使用；编辑表单本身通过 LoadHomework 显式填充
        MainDrawerContent.DataContext = homeworkItem;
        MainDrawerEditView.LoadHomework(homeworkItem, Date);
        MainDrawerScrollViewer.Offset = new Avalonia.Vector(0, 0);
        SetMainDrawerState(MainDrawerState.Opened);
    }

    /// <summary>
    /// 关闭主抽屉。科目和内容都存在且模板参数已填满时自动保存；
    /// 否则放弃本次更改并关闭抽屉。
    /// </summary>
    public void CloseMainDrawer()
    {
        if (_mainDrawerState == MainDrawerState.Closed)
            return;

        if (!MainDrawerEditView.TrySaveIfReady())
            return;

        SetMainDrawerState(MainDrawerState.Closed);
    }

    private void OnMainDrawerHomeworkSaved(HomeworkItem item, bool isNew)
    {
        MainDrawerHomeworkItem = item;
        MainDrawerContent.DataContext = item;
        HomeworkPanel.Refresh();
        ShowSuccess(
            isNew ? "作业已添加" : "作业已修改",
            $"{item.Subject}：{Summarize(item.Content)}");
    }

    private void OnMainDrawerHomeworkDeleted(HomeworkItem item)
    {
        MainDrawerHomeworkItem = null;
        HomeworkPanel.Refresh();
        ShowInfoBar(FAInfoBarSeverity.Informational, "作业已删除",
            $"已删除 {item.Subject} 的作业");
        // 删除后直接关闭抽屉，避免退出时又把表单内容保存回来
        SetMainDrawerState(MainDrawerState.Closed);
    }

    private void DrawerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_mainDrawerState == MainDrawerState.Opened)
            CloseMainDrawer();
        else
            OpenMainDrawer();
    }

    private void MainDrawerCloseButton_OnClick(object? sender, RoutedEventArgs e) =>
        CloseMainDrawer();

    private void MainDrawerDismissLayer_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
        CloseMainDrawer();

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape)
            return;

        // 时光机层级高于主抽屉，优先关闭
        if (_timeMachineState == TimeMachineState.Opened)
        {
            CloseTimeMachine();
            e.Handled = true;
            return;
        }

        if (_mainDrawerState != MainDrawerState.Opened)
            return;

        CloseMainDrawer();
        e.Handled = true;
    }

    private void SetMainDrawerState(MainDrawerState state)
    {
        if (_mainDrawerState == state)
            return;

        _mainDrawerState = state;
        _logger.Debug("主抽屉状态切换为: {State}", state);

        // 关闭时整层不参与命中测试，避免影响主界面
        MainDrawerRoot.IsHitTestVisible = state != MainDrawerState.Closed;

        // 打开时拦截外部点击并关闭抽屉
        MainDrawerDismissLayer.IsHitTestVisible = state == MainDrawerState.Opened;

        UpdateMainDrawerPosition();
    }

    private void MainDrawerCanvas_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var canvasHeight = MainDrawerCanvas.Bounds.Height;
        if (canvasHeight <= 0)
            return;

        _mainDrawerContainerHeight = canvasHeight * MainDrawerContentHeightRatio;
        MainDrawerPanel.Height = _mainDrawerContainerHeight;
        _mainDrawerLayoutInitialized = true;
        UpdateMainDrawerPosition();
    }

    private void UpdateMainDrawerPosition()
    {
        if (!_mainDrawerLayoutInitialized)
            return;

        // Opened 贴底，Closed 整体移到画布下方
        var bottom = _mainDrawerState switch
        {
            MainDrawerState.Opened => 0d,
            _ => -_mainDrawerContainerHeight - 2
        };

        Canvas.SetBottom(MainDrawerPanel, bottom);
    }

    #endregion

    #region 时光机

    /// <summary>
    /// 打开时光机，并载入当前日期的所有作业快照。
    /// 快照由 SnapshotStorageService 按创建时间从早到晚返回，控件内部也会再次排序。
    /// </summary>
    public void OpenTimeMachine()
    {
        if (_timeMachineState == TimeMachineState.Opened)
            return;

        _logger.Information("打开时光机，日期: {Date}", Date);

        _isTimeMachineActionInProgress = false;
        CancelPreviewTransitions();

        var snapshots = _snapshotStorageService.GetSnapshots(Date);
        TimeMachine.ItemsSource = snapshots;
        TimeMachineSubtitle.Text = $"{Date.Month}月{Date.Day}日 · {snapshots.Count} 个快照";

        // ItemsSource 更新后，控件会保留仍在列表中的选中项；否则自动选中最新快照。
        UpdateTimeMachineSelection(TimeMachine.SelectedSnapshot);
        SetTimeMachineState(TimeMachineState.Opened);
        TimeMachine.Focus();

        // 打开时光机时直接显示当前选中快照的作业内容，不播放切换动画。
        // 快照通过异步方式加载，优先使用缓存，避免阻塞面板上浮动画。
        _previewedSnapshot = null;
        _previewedSnapshotIndex = -1;
        _ = PreviewTimeMachineSnapshotAsync(TimeMachine.SelectedSnapshot);
    }

    /// <summary>
    /// 关闭时光机，并把主内容区恢复为当前日期实际保存的作业。
    /// </summary>
    public void CloseTimeMachine() => CloseTimeMachine(restoreContent: true);

    /// <summary>
    /// 关闭时光机。
    /// </summary>
    /// <param name="restoreContent">
    /// 是否把主内容区恢复为实际保存的作业。替换/合并完成后内容已经是最终结果，
    /// 此时传入 <see langword="false"/>，避免再闪回旧内容。
    /// </param>
    /// <param name="animateRestore">
    /// 恢复实际保存的作业时是否播放滑动动画。直接退出时光机时，选中的快照向左滑出，
    /// 最新的作业从右侧向左滑入；日期切换等场景传入 <see langword="false"/>。
    /// </param>
    private void CloseTimeMachine(bool restoreContent, bool animateRestore = true)
    {
        if (_timeMachineState == TimeMachineState.Closed || _isTimeMachineActionInProgress)
            return;

        CancelPreviewTransitions();

        // 先切换面板状态并开始收起动画，恢复内容的异步加载不会阻塞关闭。
        _previewedSnapshot = null;
        _previewedSnapshotIndex = -1;
        SetTimeMachineState(TimeMachineState.Closed);

        if (restoreContent)
        {
            if (animateRestore)
                StartRestoreHomeworkContentAnimation();
            else
                RestoreCurrentHomeworkContent();
        }
    }

    /// <summary>
    /// 直接退出时光机时，把选中的快照向左滑出，并让当前实际保存的作业从右侧滑入。
    /// </summary>
    private void StartRestoreHomeworkContentAnimation()
    {
        _ = RestoreHomeworkContentWithAnimationAsync();
    }

    private async Task RestoreHomeworkContentWithAnimationAsync()
    {
        var snapshot = TimeMachine.SelectedSnapshot;
        if (snapshot is null)
        {
            RestoreCurrentHomeworkContent();
            return;
        }

        var contentVersion = _homeworkContentVersion;

        // 快照与当前作业并行加载；两者都完成后才切换画面，避免异步结果覆盖重新打开的时光机。
        var snapshotTask = LoadSnapshotHomeworkWithLoadingAsync(snapshot.FilePath);
        var currentTask = LoadCurrentHomeworkWithLoadingAsync();
        var snapshotHomework = await snapshotTask;
        var currentHomework = await currentTask ?? new Homework(Date, []);

        if (contentVersion != _homeworkContentVersion ||
            _timeMachineState != TimeMachineState.Closed)
            return;

        if (snapshotHomework is null)
        {
            _logger.Warning("退出时光机时无法加载所选快照: {Snapshot}", snapshot.FilePath);
            ShowHomeworkContentImmediately(currentHomework);
            return;
        }

        // 取消预览后主面板可能停在过渡中间，先对齐到选中快照再播放退出动画。
        ShowHomeworkContentImmediately(snapshotHomework);
        _ = AnimateRestoreHomeworkContentAsync(currentHomework);
    }

    private async Task AnimateRestoreHomeworkContentAsync(Homework currentHomework)
    {
        try
        {
            // 选中内容向左滑出，最新内容同时从右侧向左滑入。
            await AnimateHomeworkContentAsync(currentHomework, slideRight: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "退出时光机时播放作业内容动画失败");
            // 时光机已重新打开时，新内容优先，不再回退到旧结果。
            if (_timeMachineState == TimeMachineState.Closed)
                ShowHomeworkContentImmediately(currentHomework);
        }
    }

    private void TimeMachineCloseButton_OnClick(object? sender, RoutedEventArgs e) =>
        CloseTimeMachine();

    private void TimeMachineDismissLayer_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
        CloseTimeMachine();

    private void TimeMachine_OnSelectedSnapshotChanged(SnapshotInfo? snapshot)
    {
        UpdateTimeMachineSelection(snapshot);

        // 控件重载 ItemsSource 时也会触发该事件，只有时光机打开后才切换主内容区。
        // 替换/合并动画进行期间不处理新的预览目标，避免两套动画同时修改内容。
        if (_timeMachineState != TimeMachineState.Opened || _isTimeMachineActionInProgress)
            return;

        _pendingPreviewSnapshot = snapshot;
        _hasPendingPreviewSnapshot = true;

        // 松手后立刻显示等待浮层，不等待防抖计时器或异步加载开始。
        ShowPreviewSelectionLoading();

        // 快速滚动轮盘时不要每个选中项都重建内容；等选中状态稳定后再处理。
        if (!_isPreviewTransitionRunning)
            SchedulePreviewTransition();
    }

    private void SchedulePreviewTransition()
    {
        if (_previewDebounceTimer is null)
            return;

        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private void PreviewDebounceTimer_OnTick(object? sender, EventArgs e)
    {
        _previewDebounceTimer?.Stop();

        if (_timeMachineState != TimeMachineState.Opened ||
            !_hasPendingPreviewSnapshot)
        {
            HidePreviewSelectionLoading();
            return;
        }

        // 正在播放上一段预览动画时，由动画循环继续处理等待目标，
        // 期间保持松手时出现的等待浮层。
        if (_isPreviewTransitionRunning)
            return;

        // 交给 ProcessPreviewTransitionsAsync 内部的加载流程继续维持浮层；
        // 这里先收起松手提示，避免动画阶段仍然显示“正在加载”。
        HidePreviewSelectionLoading();
        _ = ProcessPreviewTransitionsAsync();
    }

    private void UpdateTimeMachineSelection(SnapshotInfo? snapshot)
    {
        var hasSnapshot = snapshot is not null;

        TimeMachineMergeButton.IsEnabled = hasSnapshot;
        TimeMachineReplaceButton.IsEnabled = hasSnapshot;

        TimeMachineSelectionText.Text = snapshot is null
            ? "当天暂无作业快照"
            : $"已选择：{snapshot.CreatedAt:yyyy-MM-dd HH:mm:ss}";
    }

    private async void TimeMachineReplaceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isTimeMachineActionInProgress)
            return;

        var snapshot = TimeMachine.SelectedSnapshot;
        if (snapshot is null)
            return;

        _isTimeMachineActionInProgress = true;
        try
        {
            // 替换不播放动画：取消尚未完成的预览滑动，直接以最终选中的快照为准。
            CancelPreviewTransitions();
            if (_timeMachineState != TimeMachineState.Opened)
                return;

            snapshot = TimeMachine.SelectedSnapshot;
            if (snapshot is null)
                return;

            var snapshotHomework = await LoadSnapshotHomeworkWithLoadingAsync(snapshot.FilePath);
            if (snapshotHomework is null)
            {
                _logger.Warning("加载快照失败: {Snapshot}", snapshot.FilePath);
                ShowError("还原失败", "无法读取所选快照。");
                return;
            }

            var currentHomework = await LoadCurrentHomeworkWithLoadingAsync() ?? new Homework(Date, []);
            currentHomework.HomeworkItems = snapshotHomework.HomeworkItems;

            // 当前作业现在与缓存中的快照共享列表，不能继续复用该缓存对象。
            _snapshotHomeworkCache.Remove(snapshot.FilePath);

            await _storageService.SaveAsync(currentHomework);

            // 替换不播放动画：直接把主内容区同步为替换后的作业。
            ShowHomeworkContentImmediately(currentHomework);
        }
        finally
        {
            _isTimeMachineActionInProgress = false;
        }

        CloseTimeMachine(restoreContent: false);

        _logger.Information("用户选择替换作业内容，快照: {Snapshot}", snapshot.FilePath);
        ShowSuccess("作业已还原",
            $"已替换为 {snapshot.CreatedAt:yyyy-MM-dd HH:mm:ss} 的快照");
    }

    private async void TimeMachineMergeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isTimeMachineActionInProgress)
            return;

        var snapshot = TimeMachine.SelectedSnapshot;
        if (snapshot is null)
            return;

        _isTimeMachineActionInProgress = true;
        try
        {
            // 合并动画必须从选中的快照开始：取消尚未完成的预览滑动，
            // 先把主内容区立即定位到该快照，再播放向左的合并动画。
            CancelPreviewTransitions();
            if (_timeMachineState != TimeMachineState.Opened)
                return;

            snapshot = TimeMachine.SelectedSnapshot;
            if (snapshot is null)
                return;

            var snapshotHomework = await LoadSnapshotHomeworkWithLoadingAsync(snapshot.FilePath);
            if (snapshotHomework is null)
            {
                _logger.Warning("加载快照失败: {Snapshot}", snapshot.FilePath);
                ShowError("还原失败", "无法读取所选快照。");
                return;
            }

            if (!Equals(_previewedSnapshot, snapshot))
            {
                ShowHomeworkContentImmediately(snapshotHomework);
                _previewedSnapshot = snapshot;
                _previewedSnapshotIndex = FindSnapshotIndex(snapshot);
            }

            var currentHomework = await LoadCurrentHomeworkWithLoadingAsync() ?? new Homework(Date, []);
            var mergedHomework = await MergeHomeworkWithSnapshotAsync(snapshotHomework, currentHomework);
            if (mergedHomework is null || _timeMachineState != TimeMachineState.Opened)
                return;

            // 合并结果可能引用快照中的作业项，不能再把该缓存对象用于预览。
            _snapshotHomeworkCache.Remove(snapshot.FilePath);

            // 合并后内容区向左滑出，合并结果同时从右侧向左滑入。
            try
            {
                await AnimateHomeworkContentAsync(mergedHomework, slideRight: false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "合并后播放作业内容动画失败");
                ShowHomeworkContentImmediately(mergedHomework);
            }
        }
        finally
        {
            _isTimeMachineActionInProgress = false;
        }

        CloseTimeMachine(restoreContent: false);
        ShowSuccess("作业已合并",
            $"已合并 {snapshot.CreatedAt:yyyy-MM-dd HH:mm:ss} 的快照");
    }

    private void SetTimeMachineState(TimeMachineState state)
    {
        if (_timeMachineState == state)
            return;

        _timeMachineState = state;
        _logger.Debug("时光机状态切换为: {State}", state);

        // 关闭时整层不参与命中测试，避免影响主界面
        TimeMachineRoot.IsHitTestVisible = state != TimeMachineState.Closed;

        // 打开时拦截外部点击并关闭时光机
        TimeMachineDismissLayer.IsHitTestVisible = state == TimeMachineState.Opened;

        UpdateTimeMachinePosition();
    }

    private void TimeMachineCanvas_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var canvasHeight = TimeMachineCanvas.Bounds.Height;
        if (canvasHeight <= 0)
            return;

        _timeMachineContainerHeight = Math.Clamp(
            canvasHeight * TimeMachineContentHeightRatio,
            TimeMachineMinHeight,
            TimeMachineMaxHeight);
        TimeMachinePanel.Height = _timeMachineContainerHeight;
        _timeMachineLayoutInitialized = true;

        // 窗口尺寸变化时不要播放滑动动画：关闭状态直接对齐到画布下方。
        if (_timeMachineState == TimeMachineState.Closed)
        {
            StopTimeMachinePanelAnimation();
            _timeMachinePanelTransform.Y = _timeMachineContainerHeight + 2;
            return;
        }

        UpdateTimeMachinePosition();
    }

    private void UpdateTimeMachinePosition()
    {
        if (!_timeMachineLayoutInitialized)
            return;

        // 面板固定贴住画布底部，通过 RenderTransform.Y 移入/移出可视区域。
        var targetY = _timeMachineState switch
        {
            TimeMachineState.Opened => 0d,
            _ => _timeMachineContainerHeight + 2
        };

        _ = AnimateTimeMachinePanelAsync(targetY);
    }

    private async Task AnimateTimeMachinePanelAsync(double targetY)
    {
        // 取消旧动画会回退到基准值，先记录当前可见位置再取消，避免中途反向时跳变。
        var startY = _timeMachinePanelTransform.Y;
        _timeMachinePanelAnimationCts?.Cancel();
        _timeMachinePanelTransform.Y = startY;

        var cts = new CancellationTokenSource();
        _timeMachinePanelAnimationCts = cts;
        var token = cts.Token;

        try
        {
            if (Math.Abs(startY - targetY) < 0.5)
            {
                _timeMachinePanelTransform.Y = targetY;
                return;
            }

            var animation = new Animation
            {
                Duration = TimeMachinePanelSlideDuration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(TranslateTransform.YProperty, startY) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(TranslateTransform.YProperty, targetY) }
                    }
                }
            };

            await animation.RunAsync(TimeMachinePanel, token);
            if (!token.IsCancellationRequested)
                _timeMachinePanelTransform.Y = targetY;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "时光机面板滑动动画失败");
            if (!token.IsCancellationRequested)
                _timeMachinePanelTransform.Y = targetY;
        }
        finally
        {
            if (ReferenceEquals(_timeMachinePanelAnimationCts, cts))
                _timeMachinePanelAnimationCts = null;
            cts.Dispose();
        }
    }

    private void StopTimeMachinePanelAnimation()
    {
        _timeMachinePanelAnimationCts?.Cancel();
    }

    /// <summary>
    /// 显示主内容区的等待浮层。多个加载重叠时由计数控制，最后一个加载结束后才隐藏。
    /// </summary>
    private void BeginHomeworkLoading()
    {
        _homeworkLoadingCount++;
        UpdateHomeworkLoadingOverlay();
    }

    private void EndHomeworkLoading()
    {
        if (_homeworkLoadingCount > 0)
            _homeworkLoadingCount--;

        UpdateHomeworkLoadingOverlay();
    }

    /// <summary>
    /// 轮盘松手后立即显示等待浮层。此时预览还有防抖等待，尚未真正开始加载，
    /// 但用户需要马上得到“正在处理”的反馈。
    /// </summary>
    private void ShowPreviewSelectionLoading()
    {
        _previewSelectionLoading = true;
        UpdateHomeworkLoadingOverlay();
    }

    private void HidePreviewSelectionLoading()
    {
        _previewSelectionLoading = false;
        UpdateHomeworkLoadingOverlay();
    }

    private void UpdateHomeworkLoadingOverlay()
    {
        var isVisible = _homeworkLoadingCount > 0 || _previewSelectionLoading;
        HomeworkLoadingRing.IsActive = isVisible;
        HomeworkLoadingOverlay.IsVisible = isVisible;
    }

    private async Task<Homework?> LoadSnapshotHomeworkWithLoadingAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        BeginHomeworkLoading();
        try
        {
            return await GetSnapshotHomeworkAsync(filePath).WaitAsync(cancellationToken);
        }
        finally
        {
            EndHomeworkLoading();
        }
    }

    private async Task<Homework?> LoadCurrentHomeworkWithLoadingAsync(
        CancellationToken cancellationToken = default)
    {
        BeginHomeworkLoading();
        try
        {
            return await _storageService.LoadAsync(Date).WaitAsync(cancellationToken);
        }
        finally
        {
            EndHomeworkLoading();
        }
    }

    /// <summary>
    /// 异步加载指定快照并在主内容区显示，不播放动画。加载期间用户可以继续操作时光机。
    /// </summary>
    private async Task PreviewTimeMachineSnapshotAsync(SnapshotInfo? snapshot)
    {
        if (snapshot is null)
        {
            RestoreCurrentHomeworkContent();
            return;
        }

        var homework = await LoadSnapshotHomeworkWithLoadingAsync(snapshot.FilePath);
        if (homework is null)
        {
            _logger.Warning("预览快照失败，无法加载: {Snapshot}", snapshot.FilePath);
            if (_timeMachineState == TimeMachineState.Opened &&
                Equals(TimeMachine.SelectedSnapshot, snapshot))
                RestoreCurrentHomeworkContent();
            return;
        }

        // 加载期间用户可能已关闭时光机、切换了选择，或已经开始预览动画。
        if (_timeMachineState != TimeMachineState.Opened ||
            _hasPendingPreviewSnapshot ||
            _isPreviewTransitionRunning ||
            !Equals(TimeMachine.SelectedSnapshot, snapshot))
            return;

        ShowHomeworkContentImmediately(homework);
        _previewedSnapshot = snapshot;
        _previewedSnapshotIndex = FindSnapshotIndex(snapshot);
        PreloadSnapshotNeighbors(_previewedSnapshotIndex);
    }

    /// <summary>
    /// 获取快照的加载任务。同一路径只读取一次，异步任务本身也会被缓存。
    /// </summary>
    private Task<Homework?> GetSnapshotHomeworkAsync(string filePath)
    {
        if (_snapshotHomeworkCache.TryGetValue(filePath, out var cachedTask))
            return cachedTask;

        var loadTask = _storageService.LoadFromFileAsync(filePath);
        _snapshotHomeworkCache[filePath] = loadTask;
        return loadTask;
    }

    /// <summary>
    /// 预加载相邻快照，让下一次滚动选择可以立即显示。
    /// </summary>
    private void PreloadSnapshotNeighbors(int index)
    {
        var snapshots = TimeMachine.Snapshots;
        if (index < 0 || index >= snapshots.Count)
            return;

        var start = Math.Max(0, index - 1);
        var end = Math.Min(snapshots.Count - 1, index + 1);
        for (var i = start; i <= end; i++)
            _ = GetSnapshotHomeworkAsync(snapshots[i].FilePath);
    }

    /// <summary>
    /// 处理等待中的预览切换。连续切换时只保留最后一个目标。
    /// </summary>
    private async Task ProcessPreviewTransitionsAsync()
    {
        if (_isPreviewTransitionRunning)
            return;

        _isPreviewTransitionRunning = true;
        var cts = new CancellationTokenSource();
        _previewTransitionCts = cts;
        var token = cts.Token;

        try
        {
            while (_hasPendingPreviewSnapshot)
            {
                token.ThrowIfCancellationRequested();

                var target = _pendingPreviewSnapshot;
                _pendingPreviewSnapshot = null;
                _hasPendingPreviewSnapshot = false;

                // 松手提示到此结束，后续由具体的加载操作接管浮层。
                HidePreviewSelectionLoading();

                if (Equals(target, _previewedSnapshot))
                    continue;

                var targetIndex = target is null ? -1 : FindSnapshotIndex(target);
                if (target is null || targetIndex < 0)
                {
                    // 没有可预览的快照时回到实际保存的作业内容。
                    var currentHomework = await LoadCurrentHomeworkWithLoadingAsync(token);
                    token.ThrowIfCancellationRequested();
                    ShowHomeworkContentImmediately(currentHomework ?? new Homework(Date, []));
                    _previewedSnapshot = null;
                    _previewedSnapshotIndex = -1;
                    continue;
                }

                var homework = await LoadSnapshotHomeworkWithLoadingAsync(target.FilePath, token);
                if (homework is null)
                {
                    _logger.Warning("预览快照失败，无法加载: {Snapshot}", target.FilePath);
                    continue;
                }

                // 加载期间又选中了别的快照时，跳过这个中间目标，直接处理最新目标。
                if (_hasPendingPreviewSnapshot && !Equals(_pendingPreviewSnapshot, target))
                    continue;

                // 选中更早的快照时整体向右滑，选中更新的快照时整体向左滑。
                var slideRight = _previewedSnapshotIndex >= 0 &&
                                 targetIndex < _previewedSnapshotIndex;
                await AnimateHomeworkContentAsync(homework, slideRight, token);

                _previewedSnapshot = target;
                _previewedSnapshotIndex = targetIndex;
                PreloadSnapshotNeighbors(targetIndex);
            }
        }
        catch (OperationCanceledException)
        {
            // 关闭时光机或执行替换/合并时会取消正在进行的预览动画。
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "切换时光机预览失败");
            ResetHomeworkContentTransforms();
        }
        finally
        {
            // 循环结束、取消或异常时，不能把松手提示遗留在界面上。
            HidePreviewSelectionLoading();

            _isPreviewTransitionRunning = false;
            if (ReferenceEquals(_previewTransitionCts, cts))
            {
                _previewTransitionCts = null;
                cts.Dispose();
            }

            // 关闭后立刻重新打开等极端情况下，继续处理等待中的目标。
            if (_timeMachineState == TimeMachineState.Opened && _hasPendingPreviewSnapshot)
                _ = ProcessPreviewTransitionsAsync();
        }
    }

    /// <summary>
    /// 同时滑动新旧两份作业内容。旧内容从主面板滑出，新内容由预览面板滑入，
    /// 两个面板朝同一方向移动。
    /// </summary>
    private async Task AnimateHomeworkContentAsync(
        Homework incomingHomework,
        bool slideRight,
        CancellationToken cancellationToken)
    {
        var contentVersion = ++_homeworkContentVersion;
        var offset = HomeworkContentHost.Bounds.Width;
        if (!double.IsFinite(offset) || offset <= 0)
        {
            offset = Bounds.Width;
            if (!double.IsFinite(offset) || offset <= 0)
                offset = HomeworkContentSlideFallbackOffset;
        }

        var distance = slideRight ? offset : -offset;

        _homeworkPreviewPanel.DisplayHomework(incomingHomework);
        _homeworkPreviewPanel.IsVisible = true;
        _homeworkPreviewPanel.IsHitTestVisible = true;

        _homeworkContentTransform.X = 0;
        _homeworkPreviewTransform.X = -distance;

        var outgoingAnimation = CreateHomeworkContentSlideAnimation(0, distance);
        var incomingAnimation = CreateHomeworkContentSlideAnimation(-distance, 0);

        // 动画目标必须是控件本身：TransformAnimator 需要把目标转换为 Visual，
        // 再从控件的 RenderTransform 上找到对应的 TranslateTransform。
        await Task.WhenAll(
            outgoingAnimation.RunAsync(HomeworkPanel, cancellationToken),
            incomingAnimation.RunAsync(_homeworkPreviewPanel, cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();

        // 动画期间已经直接展示了更新的内容，当前结果已经过期。
        if (contentVersion != _homeworkContentVersion)
            return;

        // 主面板在屏幕外同步为新内容，然后换回原点并隐藏预览面板，
        // 最终画面与预览面板完全一致，不会出现空白帧。
        HomeworkPanel.DisplayHomework(incomingHomework);
        ResetHomeworkContentTransforms();
    }

    private static Animation CreateHomeworkContentSlideAnimation(double from, double to) =>
        new()
        {
            Duration = HomeworkContentSlideDuration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, from)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(TranslateTransform.XProperty, to)
                    }
                }
            }
        };

    /// <summary>
    /// 立即显示作业内容并复位所有滑动状态。
    /// </summary>
    private void ShowHomeworkContentImmediately(Homework homework)
    {
        // 直接展示会让正在播放的滑动动画失效。
        _homeworkContentVersion++;
        HomeworkPanel.DisplayHomework(homework);
        ResetHomeworkContentTransforms();
    }

    /// <summary>
    /// 把主内容区恢复为当前日期实际保存的作业。
    /// </summary>
    private void RestoreCurrentHomeworkContent() =>
        ShowHomeworkContentImmediately(_storageService.Load(Date) ?? new Homework(Date, []));

    /// <summary>
    /// 丢弃正在使用的位移动画，换回静止的变换，避免动画的 FillMode 残留值影响下一次显示。
    /// </summary>
    private void ResetHomeworkContentTransforms()
    {
        _homeworkContentTransform = new TranslateTransform();
        HomeworkPanel.RenderTransform = _homeworkContentTransform;

        _homeworkPreviewTransform = new TranslateTransform();
        _homeworkPreviewPanel.RenderTransform = _homeworkPreviewTransform;
        _homeworkPreviewPanel.IsVisible = false;
        _homeworkPreviewPanel.IsHitTestVisible = false;
    }

    private void CancelPreviewTransitions()
    {
        _previewDebounceTimer?.Stop();
        HidePreviewSelectionLoading();

        if (_isPreviewTransitionRunning)
        {
            _previewTransitionCts?.Cancel();

            // 取消可能发生在滑动动画中间，立即复位两个面板，避免内容区停在半滑出状态。
            ResetHomeworkContentTransforms();
        }
        else
        {
            _previewTransitionCts?.Dispose();
            _previewTransitionCts = null;
        }

        _pendingPreviewSnapshot = null;
        _hasPendingPreviewSnapshot = false;
    }

    private int FindSnapshotIndex(SnapshotInfo snapshot)
    {
        var snapshots = TimeMachine.Snapshots;
        for (var i = 0; i < snapshots.Count; i++)
        {
            if (string.Equals(snapshots[i].FilePath, snapshot.FilePath, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    #endregion

    private void AddHomeworkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击了添加作业按钮，打开主抽屉");
        OpenMainDrawer();
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowMinimized?.Invoke(this, EventArgs.Empty);
    }

    private async void ShareButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var homework = _storageService.Load(Date);
        if (homework is null) return;

        ExportFormat? exportFormat = null;
        var formatPanel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        var imageBtn = new Button { Content = "导出为图片", HorizontalAlignment = HorizontalAlignment.Stretch };
        var pdfBtn = new Button { Content = "导出为 PDF", HorizontalAlignment = HorizontalAlignment.Stretch };
        var qrBtn = new Button { Content = "通过二维码导出到其它设备", HorizontalAlignment = HorizontalAlignment.Stretch };
        var formatDialog = new FAContentDialog()
        {
            Title = "选择导出格式",
            Content = formatPanel,
            CloseButtonText = "取消"
        };
        imageBtn.Click += (_, _) => { exportFormat = ExportFormat.Image; formatDialog.Hide(); };
        pdfBtn.Click += (_, _) => { exportFormat = ExportFormat.Pdf; formatDialog.Hide(); };
        qrBtn.Click += (_, _) => { exportFormat = ExportFormat.QrCode; formatDialog.Hide(); };
        formatDialog.CloseButtonClick += (_, _) => { formatDialog.Hide(); };
        formatPanel.Children.Add(new TextBlock { Text = "请选择要导出的文件格式：" });
        formatPanel.Children.Add(imageBtn);
        formatPanel.Children.Add(pdfBtn);
        formatPanel.Children.Add(qrBtn);
        await formatDialog.ShowAsync(this);

        if (exportFormat is null) return;

        try
        {
            if (exportFormat == ExportFormat.Image)
            {
                var outputPath = $"Outputs/{Date:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.png";
                HomeworkImageService.SaveHomeworkImage(homework, outputPath);
                ShowSuccess("图片已导出", Path.GetFullPath(outputPath));
                await ShowExportResultDialog(outputPath, isImage: true);
            }
            else if (exportFormat == ExportFormat.Pdf)
            {
                var outputPath = $"Outputs/{Date:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.pdf";
                var pdfService = new UafPdfService();
                pdfService.InitializeFonts();
                var pdfBytes = pdfService.GeneratePdfFromHomework(homework);
                var fullPath = Path.GetFullPath(outputPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(fullPath, pdfBytes);
                ShowSuccess("PDF 已导出", fullPath);
                await ShowExportResultDialog(outputPath, isImage: false);
            }
            else if (exportFormat == ExportFormat.QrCode)
            {
                var outputPath = $"Outputs/{Date:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.png";
                var qrOutputPath = HomeworkQrCodeService.GenerateQrCode(homework, outputPath);
                if (qrOutputPath is null)
                {
                    ShowError("二维码导出失败", "作业数据量过大，超出了二维码的存储上限，请尝试减少作业条目数量。",
                        TimeSpan.Zero);
                    var errorDialog = new FAContentDialog()
                    {
                        Title = "二维码导出失败",
                        Content = "作业数据量过大，超出了二维码的存储上限，请尝试减少作业条目数量。",
                        CloseButtonText = "关闭"
                    };
                    errorDialog.CloseButtonClick += (_, _) => errorDialog.Hide();
                    await errorDialog.ShowAsync(this);
                }
                else
                {
                    ShowSuccess("二维码已导出", Path.GetFullPath(qrOutputPath));
                    await ShowQrCodeDialog(qrOutputPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "导出作业失败，格式: {Format}", exportFormat);
            ShowError("导出失败", ex.Message, TimeSpan.Zero);
        }
    }

    private async Task ShowExportResultDialog(string outputPath, bool isImage)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var dialog = new FAContentDialog()
        {
            Title = "作业分享",
            Content = $"今日作业已保存到 {fullPath}，请自行查看或点击复制{(isImage ? "图片" : "文件路径")}。",
            PrimaryButtonText = "打开",
            SecondaryButtonText = "复制",
            CloseButtonText = "关闭"
        };
        dialog.PrimaryButtonClick += (_, _) =>
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = fullPath,
                        UseShellExecute = false
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = fullPath,
                        UseShellExecute = false
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "打开文件失败: {Path}", fullPath);
                ShowError("打开文件失败", ex.Message, TimeSpan.Zero);
            }
            dialog.Hide();
        };
        if (isImage)
        {
            dialog.SecondaryButtonClick += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is null) return;
                _clipboardBitmap?.Dispose();
                _clipboardBitmap = new Bitmap(outputPath);
                await clipboard.SetBitmapAsync(_clipboardBitmap);
                await clipboard.FlushAsync();
                dialog.Hide();
            };
        }
        else
        {
            dialog.SecondaryButtonClick += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                {
                    await clipboard.SetTextAsync(fullPath);
                }
                dialog.Hide();
            };
        }
        dialog.CloseButtonClick += (_, _) => { dialog.Hide(); };
        await dialog.ShowAsync(this);
    }

    private async Task ShowQrCodeDialog(string qrCodePath)
    {
        var fullPath = Path.GetFullPath(qrCodePath);
        var qrBitmap = new Bitmap(fullPath);
        var image = new Image
        {
            Source = qrBitmap,
            Width = 300,
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dialog = new FAContentDialog()
        {
            Title = "二维码导出",
            Content = image,
            PrimaryButtonText = "复制",
            CloseButtonText = "关闭"
        };
        dialog.PrimaryButtonClick += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetBitmapAsync(qrBitmap);
                await clipboard.FlushAsync();
            }
            dialog.Hide();
        };
        dialog.CloseButtonClick += (_, _) => { dialog.Hide(); };
        await dialog.ShowAsync(this);
    }

    private void AutoScrollButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isAutoScrollEnabled = !_isAutoScrollEnabled;

        // 主面板和时光机预览面板共用同一开关，切换快照后也能保持自动滚动
        HomeworkPanel.IsAutoScrollEnabled = _isAutoScrollEnabled;
        _homeworkPreviewPanel.IsAutoScrollEnabled = _isAutoScrollEnabled;

        AutoScrollIcon.Symbol = _isAutoScrollEnabled ? FASymbol.Pause : FASymbol.ChevronDown;
        ToolTip.SetTip(AutoScrollButton, _isAutoScrollEnabled ? "停止自动滚动" : "自动滚动");
        _logger.Debug("自动滚动状态切换为: {Enabled}", _isAutoScrollEnabled);
    }

    private void FreezeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // 长按解冻完成后，同一次按住松开时触发的 Click 需要吞掉
        if (_suppressFreezeClick)
        {
            _suppressFreezeClick = false;
            return;
        }

        // 冻结状态下只能长按解冻，普通点击不生效
        if (_isFrozen)
            return;

        SetFrozen(true);
    }

    private void SetFrozen(bool frozen)
    {
        var homework = _storageService.Load(Date) ?? new Homework(Date, []);
        homework.Frozen = frozen;
        _storageService.Save(homework);
        _logger.Information("作业冻结状态切换为: {Frozen}", frozen);
        HomeworkPanel.IsFrozen = frozen;
    }

    #region 冻结长按解冻

    private void FreezeButton_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _suppressFreezeClick = false;

        if (!_isFrozen)
            return;

        if (!e.GetCurrentPoint(FreezeButton).Properties.IsLeftButtonPressed)
            return;

        // 显式捕获到按钮，确保按住后移出按钮再松开也能收到 Released / CaptureLost
        e.Pointer.Capture(FreezeButton);
        StartFreezeHold();
    }

    private void FreezeButton_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isFreezeHoldActive)
            return;

        // 松开时刚好达到时长，即使定时器还没来得及 tick，也按完成处理
        if (_freezeHoldStopwatch.Elapsed >= FreezeHoldDuration)
        {
            CompleteFreezeHold();
            return;
        }

        // 提前松开：取消本次解冻，并吞掉随后可能触发的普通点击
        CancelFreezeHold();
        _suppressFreezeClick = true;
    }

    private void FreezeButton_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CancelFreezeHold();
        _suppressFreezeClick = false;
    }

    private void StartFreezeHold()
    {
        _freezeHoldTimer ??= CreateFreezeHoldTimer();
        _freezeHoldTimer.Stop();
        _freezeHoldStopwatch.Restart();

        FreezeProgressRing.IsActive = true;
        SetFreezeProgressImmediate(0);

        _isFreezeHoldActive = true;
        _freezeHoldTimer.Start();
    }

    private DispatcherTimer CreateFreezeHoldTimer()
    {
        // 用 Render 优先级按帧更新圆环，Stopwatch 负责计算真实进度
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += FreezeHoldTimer_OnTick;
        return timer;
    }

    private void FreezeHoldTimer_OnTick(object? sender, EventArgs e)
    {
        var elapsed = _freezeHoldStopwatch.Elapsed;
        if (elapsed >= FreezeHoldDuration)
        {
            CompleteFreezeHold();
            return;
        }

        SetFreezeProgressImmediate(
            elapsed.TotalMilliseconds / FreezeHoldDuration.TotalMilliseconds * 100);
    }

    private void CompleteFreezeHold()
    {
        _freezeHoldTimer?.Stop();
        _freezeHoldStopwatch.Stop();
        _isFreezeHoldActive = false;

        // 让圆环先以填满状态渲染一帧，再在下一轮消息循环中隐藏
        SetFreezeProgressImmediate(99.99);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isFreezeHoldActive)
                HideFreezeProgressRing();
        }, DispatcherPriority.Background);

        SetFrozen(false);

        // 本次按住松开时 Button 仍会触发 Click，吞掉它，避免解冻后立刻又冻结
        _suppressFreezeClick = true;
    }

    private void CancelFreezeHold()
    {
        _freezeHoldTimer?.Stop();
        _freezeHoldStopwatch.Stop();
        _isFreezeHoldActive = false;
        HideFreezeProgressRing();
    }

    private void HideFreezeProgressRing()
    {
        FreezeProgressRing.IsActive = false;
        FreezeProgressRing.Value = 0;
    }

    /// <summary>
    /// FAProgressRing 对 Value 的增加内置了固定 2s 动画，无法通过公开属性调整。
    /// 先设到 100 再设回目标值，可以让圆环立即跳到目标进度，由外部计时器控制填充速度。
    /// </summary>
    private void SetFreezeProgressImmediate(double progress)
    {
        FreezeProgressRing.Value = 100;
        FreezeProgressRing.Value = progress;
    }

    #endregion

    private void ApplyFrozenState(bool frozen)
    {
        _isFrozen = frozen;
        AddHomeworkButton.IsEnabled = !frozen;
        RestoreButton.IsEnabled = !frozen;

        // 冻结时用柔和的状态提示色突出显示，解冻后恢复默认卡片背景色；
        // 前景色同步切换，保证图标在提示色背景上仍然清晰
        FreezeButton.Bind(BackgroundProperty, new DynamicResourceExtension(
            frozen ? "SystemFillColorAttentionBackgroundBrush" : "CardBackgroundFillColorDefaultBrush"));
        FreezeButton.Bind(ForegroundProperty, new DynamicResourceExtension(
            frozen ? "SystemFillColorAttentionBrush" : "TextFillColorPrimaryBrush"));
    }

    private void RestoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击了还原按钮，打开时光机");
        OpenTimeMachine();
    }

    /// <summary>
    /// 将快照与当前作业合并。返回合并后的作业；用户在冲突对话框中取消时返回
    /// <see langword="null"/>。调用方负责播放合并结果的入场动画。
    /// </summary>
    private async Task<Homework?> MergeHomeworkWithSnapshotAsync(Homework snapshotHomework, Homework currentHomework)
    {
        var conflictIds = _homeworkMergeService.PreprocessHomeworksToMerge(snapshotHomework, currentHomework);

        if (conflictIds.Count == 0)
        {
            var merged = _homeworkMergeService.MergeHomework(snapshotHomework, currentHomework, []);
            _storageService.Save(merged);
            _logger.Information("无冲突，作业已合并");
            return merged;
        }

        var viewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 800};
        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        viewer.Content = panel;
        var options = new Dictionary<Guid, HomeworkMergeOption>();

        foreach (var guid in conflictIds)
        {
            var snapshotItem = snapshotHomework.GetHomeworkItem(guid);
            var currentItem = currentHomework.GetHomeworkItem(guid);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock
            {
                Text = snapshotItem is null ? "无" : $"{snapshotItem.Subject}：{snapshotItem.Content}",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 180,
                TextWrapping = TextWrapping.Wrap
            });

            var useOldRadio = new RadioButton { Content = "以快照为准", GroupName = $"Conflict-{guid}" };
            var useNewRadio = new RadioButton { Content = "以当前作业为准", GroupName = $"Conflict-{guid}" };
            useOldRadio.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true })
                    options[guid] = HomeworkMergeOption.UseOld;
            };
            useNewRadio.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true })
                    options[guid] = HomeworkMergeOption.UseNew;
            };
            if (currentItem is null)
                useOldRadio.IsChecked = true;
            else
                useNewRadio.IsChecked = true;
            row.Children.Add(useOldRadio);
            row.Children.Add(useNewRadio);

            row.Children.Add(new TextBlock
            {
                Text = currentItem is null ? "无" : $"{currentItem.Subject}：{currentItem.Content}",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 180,
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(row);
        }

        var dialog = new FAContentDialog
        {
            Title = "解决冲突",
            Content = viewer,
            PrimaryButtonText = "合并",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(this);
        if (result != FAContentDialogResult.Primary)
        {
            _logger.Information("用户取消合并");
            return null;
        }

        var mergedHomework = _homeworkMergeService.MergeHomework(snapshotHomework, currentHomework, options);
        _storageService.Save(mergedHomework);
        _logger.Information("作业合并完成");
        return mergedHomework;
    }
}