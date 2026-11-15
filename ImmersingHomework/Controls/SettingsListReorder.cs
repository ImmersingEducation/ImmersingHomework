using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ImmersingHomework.Controls;

/// <summary>
/// 为设置页的 <see cref="SettingsListItem"/> 列表提供拖动排序。
/// 拖动时只调整可视顺序，松开后才提交到集合，避免拖拽期间重建列表。
/// </summary>
internal sealed class SettingsListReorder
{
    private const double DragStartThreshold = 6;
    private const double AutoScrollEdge = 32;
    private const double AutoScrollStep = 8;

    private readonly StackPanel _panel;
    private readonly ScrollViewer _scrollViewer;
    private readonly Action<int, int> _moveItem;

    private SettingsListItem? _source;
    private IPointer? _pointer;
    private Point _startPoint;
    private Point _lastPoint;
    private int _originalIndex;
    private int _autoScrollDirection;
    private bool _isPointerTracking;
    private bool _isDragging;
    private bool _autoScrollFrameScheduled;

    public SettingsListReorder(StackPanel panel, ScrollViewer scrollViewer, Action<int, int> moveItem)
    {
        _panel = panel;
        _scrollViewer = scrollViewer;
        _moveItem = moveItem;

        _panel.AddHandler(InputElement.PointerPressedEvent, Panel_OnPointerPressed, RoutingStrategies.Tunnel, true);
        _panel.AddHandler(InputElement.PointerMovedEvent, Panel_OnPointerMoved, RoutingStrategies.Tunnel, true);
        _panel.AddHandler(InputElement.PointerReleasedEvent, Panel_OnPointerReleased, RoutingStrategies.Tunnel, true);
        _panel.AddHandler(InputElement.PointerCaptureLostEvent, Panel_OnPointerCaptureLost, RoutingStrategies.Tunnel, true);
    }

    /// <summary>取消当前拖拽，并把行恢复到原来的视觉位置。</summary>
    public void Cancel()
    {
        if (!_isPointerTracking && !_isDragging)
            return;

        if (_isDragging && _source is not null)
        {
            var currentIndex = _panel.Children.IndexOf(_source);
            if (currentIndex >= 0 && currentIndex != _originalIndex &&
                _originalIndex >= 0 && _originalIndex < _panel.Children.Count)
            {
                _panel.Children.Move(currentIndex, _originalIndex);
            }
        }

        EndDrag();
    }

    private void Panel_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isPointerTracking || _isDragging)
            return;

        if (!e.GetCurrentPoint(_panel).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is not Visual source)
            return;

        var row = FindVisualAncestor<SettingsListItem>(source);
        if (row?.DragHandle is null || !IsDescendantOf(source, row.DragHandle))
            return;

        var index = _panel.Children.IndexOf(row);
        if (index < 0)
            return;

        _source = row;
        _originalIndex = index;
        _startPoint = e.GetPosition(_panel);
        _lastPoint = _startPoint;
        _pointer = e.Pointer;
        _isPointerTracking = true;

        e.Pointer.Capture(_panel);
        e.Handled = true;
    }

    private void Panel_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerTracking)
            return;

        if (!e.GetCurrentPoint(_panel).Properties.IsLeftButtonPressed)
        {
            Cancel();
            return;
        }

        _lastPoint = e.GetPosition(_panel);

        if (!_isDragging)
        {
            var dx = _lastPoint.X - _startPoint.X;
            var dy = _lastPoint.Y - _startPoint.Y;
            if (dx * dx + dy * dy < DragStartThreshold * DragStartThreshold)
                return;

            BeginDrag();
        }

        UpdateAutoScroll(e.GetPosition(_scrollViewer).Y);
        UpdateVisualOrder();
        e.Handled = true;
    }

    private void Panel_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerTracking)
            return;

        var wasDragging = _isDragging;
        var source = _source;
        var originalIndex = _originalIndex;
        var finalIndex = source is null ? -1 : _panel.Children.IndexOf(source);

        EndDrag();

        if (wasDragging && source is not null && finalIndex >= 0 && finalIndex != originalIndex)
            _moveItem(originalIndex, finalIndex);

        e.Handled = true;
    }

    private void Panel_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        Cancel();
    }

    private void BeginDrag()
    {
        _isDragging = true;
        if (_source is not null)
            _source.Opacity = 0.55;
    }

    private void UpdateVisualOrder()
    {
        if (_source is null)
            return;

        var currentIndex = _panel.Children.IndexOf(_source);
        if (currentIndex < 0)
            return;

        var targetIndex = 0;
        foreach (var child in _panel.Children)
        {
            if (ReferenceEquals(child, _source))
                continue;

            var center = child.Bounds.Y + child.Bounds.Height / 2;
            if (_lastPoint.Y < center)
                break;

            targetIndex++;
        }

        if (currentIndex != targetIndex)
            _panel.Children.Move(currentIndex, targetIndex);
    }

    private void UpdateAutoScroll(double pointerY)
    {
        var direction = pointerY < AutoScrollEdge
            ? -1
            : pointerY > _scrollViewer.Bounds.Height - AutoScrollEdge
                ? 1
                : 0;

        if (direction == _autoScrollDirection)
            return;

        _autoScrollDirection = direction;
        if (direction != 0)
            ScheduleAutoScrollFrame();
    }

    private void ScheduleAutoScrollFrame()
    {
        if (_autoScrollFrameScheduled)
            return;

        var topLevel = TopLevel.GetTopLevel(_panel);
        if (topLevel is null)
            return;

        _autoScrollFrameScheduled = true;
        topLevel.RequestAnimationFrame(_ => AutoScrollFrame());
    }

    private void AutoScrollFrame()
    {
        _autoScrollFrameScheduled = false;
        if (!_isDragging || _autoScrollDirection == 0)
            return;

        var maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        if (maxOffset <= 0)
        {
            _autoScrollDirection = 0;
            return;
        }

        var oldOffset = _scrollViewer.Offset.Y;
        var newOffset = Math.Clamp(oldOffset + _autoScrollDirection * AutoScrollStep, 0, maxOffset);
        var appliedOffset = newOffset - oldOffset;
        if (Math.Abs(appliedOffset) < 0.01)
        {
            _autoScrollDirection = 0;
            return;
        }

        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, newOffset);
        _lastPoint = new Point(_lastPoint.X, _lastPoint.Y + appliedOffset);
        UpdateVisualOrder();
        ScheduleAutoScrollFrame();
    }

    private void EndDrag()
    {
        _autoScrollDirection = 0;

        if (_source is not null)
            _source.Opacity = 1;

        var pointer = _pointer;
        _source = null;
        _pointer = null;
        _isPointerTracking = false;
        _isDragging = false;
        pointer?.Capture(null);
    }

    private static T? FindVisualAncestor<T>(Visual? visual) where T : Visual
    {
        while (visual is not null)
        {
            if (visual is T match)
                return match;
            visual = visual.GetVisualParent();
        }

        return null;
    }

    private static bool IsDescendantOf(Visual? source, Visual ancestor)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, ancestor))
                return true;
            source = source.GetVisualParent();
        }

        return false;
    }
}
