using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Behaviors;

public class ListBoxDragDropBehavior : Behavior<ListBox>
{
    private RenderTaskViewModel? _dragItem;
    private Point _startPoint;
    private Point _dragItemStartPosition;
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

        // 清除之前的 drop target 状态（如果有的话）
        ClearDropTarget();

        // 设置拖拽状态
        _dragItem.IsDragTarget = true;
        SetDragTargetVisual(_dragItem, true);

        _startPoint = e.GetPosition(AssociatedObject.GetVisualRoot() as Visual);
        
        // 记录拖拽项目在 ListBox 中的初始位置
        _dragItemStartPosition = e.GetPosition(AssociatedObject);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem == null) return;

        var currentPosition = e.GetPosition(AssociatedObject.GetVisualRoot() as Visual);
        var isThresholdExceeded = IsDragThresholdExceeded(currentPosition);

        // 如果达到拖拽阈值，开始拖拽模式
        if (isThresholdExceeded && !_isDragging)
        {
            _isDragging = true;
            var viewModel = AssociatedObject.DataContext as RenderQueueViewModel;
            AssociatedObject.Cursor = viewModel is { CanModifyTasks: false }
                ? new Cursor(StandardCursorType.No)
                : new Cursor(StandardCursorType.DragMove);
        }

        // 无论是否达到阈值，都更新拖拽项目的变换（提供即时视觉反馈）
        UpdateDragItemTransform(e);

        // 只有在拖拽模式下才处理 drop target
        if (_isDragging)
        {
            // 处理 drop target
            var dropItem = GetMouseOverItem(sender, e);
            UpdateDropTarget(dropItem);
        }
        else
        {
            // 如果没达到阈值，清除所有 drop target 状态
            ClearDropTarget();
        }

        if (IsPointerOutsideListBox(e) || _dragItem == null)
        {
            AssociatedObject.Cursor = new Cursor(StandardCursorType.No);
            return;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem == null) return;

        var dropItem = GetMouseOverItem(sender, e);

        if (dropItem == null || dropItem == _dragItem)
        {
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
                if (dataContext != null && dataContext != _dragItem) // 排除拖拽项目本身
                {
                    return dataContext;
                }
            }
        }

        // 如果通过视觉树找不到，尝试通过位置计算找到最接近的项目
        return GetClosestItemByPosition(point);
    }

    private RenderTaskViewModel? GetClosestItemByPosition(Point point)
    {
        if (AssociatedObject == null) return null;

        var closestItem = (RenderTaskViewModel?)null;
        var closestDistance = double.MaxValue;

        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            var dataContext = listBoxItem.DataContext as RenderTaskViewModel;
            if (dataContext != null && dataContext != _dragItem)
            {
                var itemBounds = listBoxItem.Bounds;
                var itemCenter = new Point(itemBounds.X + itemBounds.Width / 2, itemBounds.Y + itemBounds.Height / 2);
                
                var distance = Math.Sqrt(Math.Pow(point.X - itemCenter.X, 2) + Math.Pow(point.Y - itemCenter.Y, 2));
                
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestItem = dataContext;
                }
            }
        }

        return closestItem;
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
        return distance >= 5; // 降低阈值，让拖拽更容易触发
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
                
                // 如果是拖拽目标，添加跟随鼠标的变换
                if (isDragTarget)
                {
                    // 设置初始变换
                    listBoxItem.RenderTransform = new TranslateTransform(0, 0);
                }
                else
                {
                    // 清除变换
                    listBoxItem.RenderTransform = null;
                }
                break;
            }
        }
    }

    private void UpdateDragItemTransform(PointerEventArgs e)
    {
        if (_dragItem == null || AssociatedObject == null) return;

        // 查找对应的 ListBoxItem
        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            if (listBoxItem.DataContext == _dragItem)
            {
                // 获取当前鼠标在 ListBox 中的位置
                var currentMousePosition = e.GetPosition(AssociatedObject);
                
                // 计算鼠标移动的偏移量
                var deltaX = currentMousePosition.X - _dragItemStartPosition.X;
                var deltaY = currentMousePosition.Y - _dragItemStartPosition.Y;
                
                // 确保变换对象存在
                if (listBoxItem.RenderTransform is not TranslateTransform)
                {
                    listBoxItem.RenderTransform = new TranslateTransform(0, 0);
                }
                
                // 应用变换
                if (listBoxItem.RenderTransform is TranslateTransform translateTransform)
                {
                    translateTransform.X = deltaX;
                    translateTransform.Y = deltaY;
                }
                break;
            }
        }
    }

    private void UpdateDropTarget(RenderTaskViewModel? dropItem)
    {
        if (dropItem != null && dropItem != _dragItem)
        {
            // 如果找到了新的 drop target
            if (_previousDropItem != null && _previousDropItem != dropItem) 
            {
                // 清除之前的 drop target
                _previousDropItem.IsDropTarget = false;
                SetDropTargetVisual(_previousDropItem, false);
            }

            // 设置新的 drop target
            dropItem.IsDropTarget = true;
            SetDropTargetVisual(dropItem, true);
            _previousDropItem = dropItem;
        }
        else
        {
            // 如果没有找到有效的 drop target，清除当前状态
            ClearDropTarget();
        }
    }

    private void ClearDropTarget()
    {
        if (_previousDropItem != null)
        {
            _previousDropItem.IsDropTarget = false;
            SetDropTargetVisual(_previousDropItem, false);
            _previousDropItem = null;
        }
    }

    private void ResetDragState()
    {
        // 清除拖拽项目状态
        if (_dragItem != null)
        {
            _dragItem.IsDragTarget = false;
            SetDragTargetVisual(_dragItem, false);
        }
        
        // 清除 drop target 状态
        ClearDropTarget();
        
        // 重置所有拖拽相关变量
        _dragItem = null;
        _isDragging = false;
        _startPoint = new Point(0, 0);
        _dragItemStartPosition = new Point(0, 0);
        
        // 重置光标
        if (AssociatedObject != null)
        {
            AssociatedObject.Cursor = Cursor.Default;
        }
    }
}