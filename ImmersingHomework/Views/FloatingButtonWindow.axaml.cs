using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Serilog;

namespace ImmersingHomework.Views;

public partial class FloatingButtonWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<FloatingButtonWindow>();
    private bool _isDragging;
    private bool _hasMoved;
    private Point _dragStartPoint;
    private PixelPoint _dragStartWindowPosition;
    private const double DragThreshold = 5; // 拖动阈值，超过此距离才认为是拖动
    private const double SnapThreshold = 30; // 边缘吸附阈值
    
    public event EventHandler? FloatingButtonClicked;
    
    public FloatingButtonWindow()
    {
        _logger.Debug("FloatingButtonWindow 初始化");
        InitializeComponent();
        
        Position = new PixelPoint(100, 100);
        
        Opacity = 0;
        
        this.PointerPressed += OnPointerPressed;
        this.PointerMoved += OnPointerMoved;
        this.PointerReleased += OnPointerReleased;
        
        _logger.Debug("浮窗初始位置: {Position}", Position);
    }

    public void ShowWithAnimation()
    {
        _logger.Information("显示浮窗");
        Show();
        AnimateOpacity(1, TimeSpan.FromMilliseconds(200));
    }

    public void HideWithAnimation()
    {
        _logger.Information("隐藏浮窗");
        AnimateOpacity(0, TimeSpan.FromMilliseconds(200), () => Hide());
    }

    private void AnimateOpacity(double targetOpacity, TimeSpan duration, Action? onComplete = null)
    {
        _logger.Debug("执行透明度动画，目标: {TargetOpacity}", targetOpacity);
        var startOpacity = Opacity;
        var startTime = DateTime.Now;
        
        Dispatcher.UIThread.Post(async () =>
        {
            while (DateTime.Now - startTime < duration)
            {
                var progress = (DateTime.Now - startTime).TotalMilliseconds / duration.TotalMilliseconds;
                Opacity = startOpacity + (targetOpacity - startOpacity) * progress;
                await Task.Delay(16);
            }
            Opacity = targetOpacity;
            _logger.Debug("透明度动画完成");
            onComplete?.Invoke();
        });
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _logger.Debug("浮窗被按下");
            _isDragging = true;
            _hasMoved = false;
            _dragStartPoint = e.GetPosition(this);
            _dragStartWindowPosition = Position;
            e.Pointer.Capture(this);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && e.Pointer.Captured == this)
        {
            var currentPoint = e.GetPosition(this);
            var delta = currentPoint - _dragStartPoint;
            
            // 检查是否超过拖动阈值
            if (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold)
            {
                _hasMoved = true;
            }
            
            var newX = _dragStartWindowPosition.X + (int)delta.X;
            var newY = _dragStartWindowPosition.Y + (int)delta.Y;
            
            // 获取屏幕尺寸并应用边界限制和边缘吸附
            if (Screens.Primary != null)
            {
                var screenBounds = Screens.Primary.Bounds;
                var windowWidth = (int)Bounds.Width;
                var windowHeight = (int)Bounds.Height;
                
                // 边界限制
                newX = Math.Max(0, Math.Min(newX, screenBounds.Width - windowWidth));
                newY = Math.Max(0, Math.Min(newY, screenBounds.Height - windowHeight));
                
                // 边缘吸附
                if (newX < SnapThreshold)
                    newX = 0;
                else if (newX > screenBounds.Width - windowWidth - SnapThreshold)
                    newX = screenBounds.Width - windowWidth;
                
                if (newY < SnapThreshold)
                    newY = 0;
                else if (newY > screenBounds.Height - windowHeight - SnapThreshold)
                    newY = screenBounds.Height - windowHeight;
            }
            
            Position = new PixelPoint(newX, newY);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && e.Pointer.Captured == this)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            
            // 如果没有移动，则触发点击事件
            if (!_hasMoved)
            {
                _logger.Debug("浮窗被点击");
                FloatingButtonClicked?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _logger.Debug("浮窗拖拽结束，新位置: {Position}", Position);
            }
        }
    }

    private void FloatingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // 这个事件处理程序保留但不使用，因为我们现在在 OnPointerReleased 中处理
        // 这样可以避免与拖动操作冲突
    }
}
