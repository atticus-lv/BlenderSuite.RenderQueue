using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Behaviors;

public class ListBoxDragDropBehavior : Behavior<ListBox>
{
    private RenderTaskViewModel? _dragItem;
    private Point _startPoint;
    private RenderTaskViewModel? _previousDropItem;
    private bool _isDragging;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is null) return;

        AssociatedObject.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        AssociatedObject.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved);
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject is null) return;

        AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 检查是否点击在拖拽手柄上，如果不是则忽略拖拽
        if (!IsClickOnDragHandle(e))
        {
            return;
        }
        
        _dragItem = GetMouseOverItem(sender, e);
        if (_dragItem == null)
        {
            return;
        }

        // 设置拖拽状态
        SetDragTargetVisual(_dragItem, true);
        Console.WriteLine($"[DragDrop Debug] Start dragging: {System.IO.Path.GetFileName(_dragItem.BlendFilePath)}");

        _startPoint = e.GetPosition(AssociatedObject.GetVisualRoot() as Visual);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem == null) return;

        var dropItem = GetMouseOverItem(sender, e);
        if (dropItem != null && dropItem != _dragItem)
        {
            if (_previousDropItem != null && _previousDropItem != dropItem) 
            {
                Console.WriteLine($"[DragDrop Debug] Clear previous drop target: {System.IO.Path.GetFileName(_previousDropItem.BlendFilePath)}");
                SetDropTargetVisual(_previousDropItem, false);
            }

            if (_previousDropItem != dropItem)
            {
                Console.WriteLine($"[DragDrop Debug] New drop target: {System.IO.Path.GetFileName(dropItem.BlendFilePath)}");
            }

            SetDropTargetVisual(dropItem, true);
            _previousDropItem = dropItem;
        }
        else
        {
            if (_previousDropItem != null)
            {
                Console.WriteLine($"[DragDrop Debug] Clear drop target (mouse outside valid area): {System.IO.Path.GetFileName(_previousDropItem.BlendFilePath)}");
                SetDropTargetVisual(_previousDropItem, false);
                _previousDropItem = null;
            }
        }

        var currentPosition = e.GetPosition(AssociatedObject.GetVisualRoot() as Visual);
        
        // 检查是否超过拖拽阈值
        if (!IsDragThresholdExceeded(currentPosition))
        {
            AssociatedObject.Cursor = Cursor.Default;
            return;
        }

        if (!_isDragging) _isDragging = true;

        // 检查是否在ListBox外部
        if (IsPointerOutsideListBox(e) || _dragItem == null)
        {
            AssociatedObject.Cursor = new Cursor(StandardCursorType.No);
            return;
        }

        // 设置拖拽光标
        var viewModel = AssociatedObject.DataContext as RenderQueueViewModel;
        AssociatedObject.Cursor = viewModel is { CanModifyTasks: false }
            ? new Cursor(StandardCursorType.No)
            : new Cursor(StandardCursorType.DragMove);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem == null) return;

        var dropItem = GetMouseOverItem(sender, e);

        if (dropItem == null || dropItem == _dragItem)
        {
            Console.WriteLine($"[DragDrop Debug] Drag cancelled - invalid drop target");
            ResetDragState();
            return;
        }

        var viewModel = AssociatedObject.DataContext as RenderQueueViewModel;
        if (viewModel != null)
        {
            if (!viewModel.CanModifyTasks)
            {
                ResetDragState();
                return;
            }

            var dragIndex = viewModel.RenderTasks.IndexOf(_dragItem);
            var dropIndex = viewModel.RenderTasks.IndexOf(dropItem);

            if (dragIndex >= 0 && dropIndex >= 0)
            {
                Console.WriteLine($"[DragDrop Debug] Execute drag operation: {System.IO.Path.GetFileName(_dragItem.BlendFilePath)} from position {dragIndex} to position {dropIndex}");
                
                // 记录当前选中的任务
                var currentlySelected = viewModel.SelectedTask;
                
                viewModel.RenderTasks.Move(dragIndex, dropIndex);
                
                // 如果当前选中的任务就是被拖拽的任务，保持选中状态
                if (currentlySelected == _dragItem)
                {
                    viewModel.SelectedTask = _dragItem;
                }
                // 否则保持原来的选中状态不变
            }
        }

        ResetDragState();
    }

    private bool IsClickOnDragHandle(PointerEventArgs e)
    {
        var point = e.GetPosition((Visual)AssociatedObject);
        var visuals = AssociatedObject.GetVisualsAt(point).ToList();

        // 检查是否点击在Tag为"DragHandle"的Border上
        foreach (var visual in visuals)
        {
            // 检查是否是Border控件且Tag为"DragHandle"
            if (visual is Border border && border.Tag?.ToString() == "DragHandle")
            {
                return true;
            }
        }

        return false;
    }

    private RenderTaskViewModel? GetMouseOverItem(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition((Visual)sender);
        var visuals = ((Visual)sender).GetVisualsAt(point).ToList();

        // 查找ListBoxItem，支持整个item区域作为drop目标
        foreach (var visual in visuals)
        {
            var listBoxItem = visual.GetLogicalAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (listBoxItem != null)
            {
                var dataContext = listBoxItem.DataContext as RenderTaskViewModel;
                if (dataContext != null)
                {
                    return dataContext;
                }
            }
        }

        return null;
    }

    private bool IsPointerOutsideListBox(PointerEventArgs e)
    {
        var position = e.GetPosition(AssociatedObject);
        return position.X < 0 || position.Y < 0 ||
               position.X > AssociatedObject.Bounds.Width ||
               position.Y > AssociatedObject.Bounds.Height;
    }

    private bool IsDragThresholdExceeded(Point currentPosition)
    {
        var deltaX = currentPosition.X - _startPoint.X;
        var deltaY = currentPosition.Y - _startPoint.Y;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return distance >= 5; // 降低阈值，提高响应性
    }

    private void SetDropTargetVisual(RenderTaskViewModel item, bool isDropTarget)
    {
        if (AssociatedObject == null) return;
        
        // 查找对应的 ListBoxItem
        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            if (listBoxItem.DataContext == item)
            {
                DropTargetBehavior.SetIsDropTarget(listBoxItem, isDropTarget);
                break;
            }
        }
    }

    private void SetDragTargetVisual(RenderTaskViewModel item, bool isDragTarget)
    {
        if (AssociatedObject == null) return;
        
        // 查找对应的 ListBoxItem
        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            if (listBoxItem.DataContext == item)
            {
                DropTargetBehavior.SetIsDragTarget(listBoxItem, isDragTarget);
                break;
            }
        }
    }

    private void ResetDragState()
    {
        // 清除拖拽状态
        if (_dragItem != null)
        {
            SetDragTargetVisual(_dragItem, false);
        }
        
        // 清除所有可能的拖拽目标状态
        if (_previousDropItem != null)
        {
            SetDropTargetVisual(_previousDropItem, false);
        }
        
        // 清理所有ListBoxItem的拖拽状态，防止状态残留
        if (AssociatedObject != null)
        {
            foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
            {
                DropTargetBehavior.SetIsDropTarget(listBoxItem, false);
                DropTargetBehavior.SetIsDragTarget(listBoxItem, false);
            }
        }
        
        _dragItem = null;
        _previousDropItem = null;
        _isDragging = false;
        AssociatedObject.Cursor = Cursor.Default;
    }
}