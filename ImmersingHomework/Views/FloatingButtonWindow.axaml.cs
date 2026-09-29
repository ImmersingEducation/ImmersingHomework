using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views;

public partial class FloatingButtonWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<FloatingButtonWindow>();

    /// <summary>拖动死区：两轴位移都小于该值（物理像素）时仍按点击处理。</summary>
    private const int DragDeadZonePixels = 4;

    private const int OpacityAnimationMilliseconds = 200;
    private const int AnimationFrameMilliseconds = 15;

    // 拖动状态：两段式，"按下"与"越过死区"分开，才能把点击和拖动区分开
    private bool _isPendingDrag;            // 已按下但尚未越过死区（此时还没有捕获指针）
    private bool _isDragging;               // 正在拖动（此时已捕获指针）
    private PixelPoint _dragStartPosition;     // 按下时的窗口位置（物理像素）
    private PixelPoint _dragStartScreenPoint;  // 按下时的指针虚拟桌面坐标（物理像素）
    private double _dragRenderScaling;         // 按下时的 DPI：拖动中用它换算，避免跨不同缩放比的屏幕时跳变
    private Screen? _dragScreen;               // 本次拖动的"源屏"，跨屏后迁移
    private IPointer? _capturedPointer;

    private int _opacityAnimationRevision;     // 手写 cancel token：让过期的淡入淡出动画立刻退出

    public event EventHandler? FloatingButtonClicked;

    public FloatingButtonWindow()
    {
        _logger.Debug("FloatingButtonWindow 初始化");
        InitializeComponent();

        Position = new PixelPoint(
            AppSettings.Instance.FloatingButtonPositionX.Value,
            AppSettings.Instance.FloatingButtonPositionY.Value);
        Opacity = 0;

        // 三个 Tunnel 事件 + handledEventsToo：整个窗口（含透明区域）都是拖动热区，
        // 并且保证在子控件之前拿到事件
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        // 指针捕获被抢走 / 窗口中途隐藏时，释放事件不会再来，必须有统一的取消路径
        AddHandler(PointerCaptureLostEvent, (_, _) => CancelDrag(), RoutingStrategies.Tunnel);
        PropertyChanged += OnSelfPropertyChanged;
        Opened += (_, _) => Dispatcher.UIThread.Post(EnsurePositionVisible, DispatcherPriority.Render);

        _logger.Debug("浮窗初始位置: {Position}", Position);
    }

    public void ShowWithAnimation()
    {
        _logger.Information("显示浮窗");
        Show();
        AnimateOpacity(1, OpacityAnimationMilliseconds);
    }

    public void HideWithAnimation()
    {
        _logger.Information("隐藏浮窗");
        AnimateOpacity(0, OpacityAnimationMilliseconds, Hide);
    }

    #region 指针拖动

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // 按下时**不**捕获指针：没越过死区就松手，事件能正常派发，浮窗被点中
        _isPendingDrag = true;
        _isDragging = false;
        _dragStartPosition = Position;
        _dragRenderScaling = RenderScaling;
        _dragStartScreenPoint = Position + ToPixelPoint(e.GetPosition(this), _dragRenderScaling);
        _dragScreen = GetScreenForWindow(Position);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPendingDrag)
            return;

        // 兜底：越过死区前没有捕获指针，若在窗口外松手，释放事件不会回到本窗口，
        // 按下状态会残留。此时不能继续拖，否则鼠标不按着浮窗也会跑。
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CancelDrag();
            return;
        }

        // 指针的虚拟桌面物理坐标：DIP 乘以下按时记录的 DPI
        var pointerPosition = Position + ToPixelPoint(e.GetPosition(this), _dragRenderScaling);
        var deltaX = pointerPosition.X - _dragStartScreenPoint.X;
        var deltaY = pointerPosition.Y - _dragStartScreenPoint.Y;
        if (!_isDragging && Math.Abs(deltaX) < DragDeadZonePixels && Math.Abs(deltaY) < DragDeadZonePixels)
            return;

        // 越过死区的这一刻才捕获指针，并阻断子控件继续处理移动
        if (!_isDragging)
        {
            _isDragging = true;
            _capturedPointer = e.Pointer;
            e.Pointer.Capture(this);
        }

        e.Handled = true;

        // 位置 = 起点 + 增量（而非逐帧累加），免疫像素取整与 DPI 量化带来的抖动累积
        Position = ConstrainDragPosition(
            new PixelPoint(_dragStartPosition.X + deltaX, _dragStartPosition.Y + deltaY),
            pointerPosition);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPendingDrag)
            return;

        _isPendingDrag = false;

        if (!_isDragging)
        {
            // 没越过死区 → 视为点击（浮窗根节点是 Border，拿不到 Button.Click）
            _logger.Debug("浮窗被点击");
            FloatingButtonClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        _isDragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;

        // 只在松手时写配置，拖动过程中一帧都不落盘
        _logger.Debug("浮窗拖拽结束，新位置: {Position}", Position);
        SavePosition();
    }

    /// <summary>统一的拖动取消路径：复位状态并释放指针捕获。</summary>
    private void CancelDrag()
    {
        if (!_isPendingDrag && !_isDragging)
            return;

        _logger.Debug("浮窗拖动被取消");
        _isPendingDrag = false;
        _isDragging = false;
        _dragScreen = null;
        if (_capturedPointer is not null)
        {
            ReleasePointerCapture(_capturedPointer);
            _capturedPointer = null;
        }
    }

    private void ReleasePointerCapture(IPointer pointer)
    {
        if (pointer.Captured == this)
            pointer.Capture(null);
        if (ReferenceEquals(_capturedPointer, pointer))
            _capturedPointer = null;
    }

    private void OnSelfPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty)
            CancelDrag();
    }

    #endregion

    #region 屏幕约束

    private static PixelPoint ToPixelPoint(Point point, double renderScaling)
    {
        return new PixelPoint(
            (int)Math.Round(point.X * renderScaling),
            (int)Math.Round(point.Y * renderScaling));
    }

    /// <summary>窗口的物理像素尺寸；布局尚未完成时回退到 XAML 里写死的 Width/Height。</summary>
    private (int Width, int Height) GetPixelSize()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        var height = Bounds.Height > 0 ? Bounds.Height : Height;
        return (Math.Max(1, (int)Math.Ceiling(width * RenderScaling)),
                Math.Max(1, (int)Math.Ceiling(height * RenderScaling)));
    }

    /// <summary>
    /// 把请求位置钳制到合法范围：指针跨到相邻屏时按两块工作区的并集/交集钳制，
    /// 否则按源屏工作区钳制。浮窗不允许停到屏幕外或两块屏之间的"死区"里。
    /// </summary>
    private PixelPoint ConstrainDragPosition(PixelPoint requestedPosition, PixelPoint pointerPosition)
    {
        var (width, height) = GetPixelSize();
        var sourceScreen = _dragScreen;
        var targetScreen = GetScreenAt(pointerPosition);

        if (sourceScreen is not null && targetScreen is not null &&
            TryConstrainAcrossAdjacentScreens(
                requestedPosition, width, height,
                sourceScreen.Bounds, targetScreen.Bounds,
                sourceScreen.WorkingArea, targetScreen.WorkingArea,
                out var acrossPosition))
        {
            // 完全进入目标屏工作区后，才把"源屏"迁移过去
            if (IsWithinWorkingArea(acrossPosition, width, height, targetScreen.WorkingArea))
                _dragScreen = targetScreen;
            return acrossPosition;
        }

        var workingArea = (sourceScreen ?? Screens.Primary)?.WorkingArea;
        return workingArea is null
            ? requestedPosition
            : ClampToWorkingArea(requestedPosition, width, height, workingArea.Value);
    }

    private static bool TryConstrainAcrossAdjacentScreens(
        PixelPoint requestedPosition,
        int width,
        int height,
        PixelRect sourceBounds,
        PixelRect targetBounds,
        PixelRect sourceWorkingArea,
        PixelRect targetWorkingArea,
        out PixelPoint constrainedPosition)
    {
        // 水平相邻：X 取两块工作区的并集，Y 取交集
        if (sourceBounds.Right == targetBounds.X || targetBounds.Right == sourceBounds.X)
        {
            var top = Math.Max(sourceWorkingArea.Y, targetWorkingArea.Y);
            var bottom = Math.Min(sourceWorkingArea.Bottom, targetWorkingArea.Bottom);
            if (bottom - top < height)
            {
                constrainedPosition = default;
                return false;
            }

            constrainedPosition = new PixelPoint(
                Math.Clamp(
                    requestedPosition.X,
                    Math.Min(sourceWorkingArea.X, targetWorkingArea.X),
                    Math.Max(sourceWorkingArea.Right, targetWorkingArea.Right) - width),
                Math.Clamp(requestedPosition.Y, top, bottom - height));
            return true;
        }

        // 垂直相邻：Y 取并集，X 取交集
        if (sourceBounds.Bottom == targetBounds.Y || targetBounds.Bottom == sourceBounds.Y)
        {
            var left = Math.Max(sourceWorkingArea.X, targetWorkingArea.X);
            var right = Math.Min(sourceWorkingArea.Right, targetWorkingArea.Right);
            if (right - left < width)
            {
                constrainedPosition = default;
                return false;
            }

            constrainedPosition = new PixelPoint(
                Math.Clamp(requestedPosition.X, left, right - width),
                Math.Clamp(
                    requestedPosition.Y,
                    Math.Min(sourceWorkingArea.Y, targetWorkingArea.Y),
                    Math.Max(sourceWorkingArea.Bottom, targetWorkingArea.Bottom) - height));
            return true;
        }

        constrainedPosition = default;
        return false;
    }

    private static PixelPoint ClampToWorkingArea(PixelPoint position, int width, int height, PixelRect workingArea)
    {
        return new PixelPoint(
            Math.Clamp(position.X, workingArea.X, Math.Max(workingArea.X, workingArea.Right - width)),
            Math.Clamp(position.Y, workingArea.Y, Math.Max(workingArea.Y, workingArea.Bottom - height)));
    }

    private static bool IsWithinWorkingArea(PixelPoint position, int width, int height, PixelRect workingArea)
    {
        return position.X >= workingArea.X
            && position.X + width <= workingArea.Right
            && position.Y >= workingArea.Y
            && position.Y + height <= workingArea.Bottom;
    }

    private Screen? GetScreenAt(PixelPoint point)
    {
        foreach (var screen in Screens.All)
        {
            var area = screen.Bounds;
            if (point.X >= area.X && point.X < area.Right &&
                point.Y >= area.Y && point.Y < area.Bottom)
                return screen;
        }

        return null;
    }

    private Screen? GetScreenForWindow(PixelPoint position, int? width = null, int? height = null)
    {
        var (defaultWidth, defaultHeight) = GetPixelSize();
        var center = new PixelPoint(
            position.X + (width ?? defaultWidth) / 2,
            position.Y + (height ?? defaultHeight) / 2);
        return GetScreenAt(center)
            ?? GetScreenAt(position)
            ?? Screens.ScreenFromPoint(center)
            ?? Screens.Primary;
    }

    private bool IsFullyVisibleOnAnyScreen(PixelPoint position, int width, int height)
    {
        foreach (var screen in Screens.All)
        {
            if (IsWithinWorkingArea(position, width, height, screen.WorkingArea))
                return true;
        }

        return false;
    }

    /// <summary>启动纠偏：保存的位置不在任何屏幕工作区内（换过显示器 / 改过分辨率）时拉回来。</summary>
    private void EnsurePositionVisible()
    {
        var (width, height) = GetPixelSize();
        if (IsFullyVisibleOnAnyScreen(Position, width, height))
            return;

        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        _logger.Information("浮窗保存的位置不在任何屏幕工作区内，已纠正: {OldPosition}", Position);
        Position = ClampToWorkingArea(Position, width, height, workingArea.Value);
        SavePosition();
    }

    #endregion

    #region 持久化与淡入淡出

    private void SavePosition()
    {
        // AppSettings 内部已有 300ms 防抖（MarkDirty），拖动结束时写一次即可
        AppSettings.Instance.FloatingButtonPositionX.Value = Position.X;
        AppSettings.Instance.FloatingButtonPositionY.Value = Position.Y;
    }

    private void AnimateOpacity(double targetOpacity, int durationMilliseconds, Action? onComplete = null)
    {
        // 先自增修订号，作废上一次还没跑完的动画
        var revision = ++_opacityAnimationRevision;
        if (durationMilliseconds <= 0)
        {
            Opacity = targetOpacity;
            onComplete?.Invoke();
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var startOpacity = Opacity;
                var startTime = Environment.TickCount64;
                while (revision == _opacityAnimationRevision)
                {
                    var progress = (Environment.TickCount64 - startTime) / (double)durationMilliseconds;
                    if (progress >= 1)
                        break;
                    Opacity = startOpacity + (targetOpacity - startOpacity) * progress;
                    await Task.Delay(AnimationFrameMilliseconds);
                }

                // 已被新的动画接管，交棒即可（下一个动画以当前 Opacity 为起点）
                if (revision != _opacityAnimationRevision)
                    return;

                Opacity = targetOpacity;
                onComplete?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "浮窗透明度动画异常");
            }
        });
    }

    #endregion
}
