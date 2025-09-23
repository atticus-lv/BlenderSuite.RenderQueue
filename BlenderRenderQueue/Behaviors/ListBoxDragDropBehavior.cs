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
        Console.WriteLine("OnPointerPressed: Starting pointer pressed event");
        
        // 检查是否点击在拖拽手柄上，如果不是则忽略拖拽
        if (!IsClickOnDragHandle(e))
        {
            Console.WriteLine("OnPointerPressed: Not clicking on drag handle, ignoring");
            return;
        }
        
        Console.WriteLine("OnPointerPressed: Clicking on drag handle");
        
        // 无论点击在拖拽手柄的哪个部分，都要找到对应的 ListBoxItem
        _dragItem = GetDragItemFromHandle(e);
        if (_dragItem == null)
        {
            Console.WriteLine("OnPointerPressed: Cannot find drag item, ignoring");
            return;
        }

        Console.WriteLine($"OnPointerPressed: Found drag item: {_dragItem.BlendFileName}");

        // 清除之前的 drop target 状态（如果有的话）
        ClearDropTarget();

        // 设置拖拽状态
        _dragItem.IsDragTarget = true;
        SetDragTargetVisual(_dragItem, true);

        _startPoint = e.GetPosition(AssociatedObject.GetVisualRoot() as Visual);
        
        // 记录拖拽项目在 ListBox 中的初始位置
        _dragItemStartPosition = e.GetPosition(AssociatedObject);
        
        Console.WriteLine($"OnPointerPressed: Drag state set, start point: {_startPoint}");
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
            Console.WriteLine($"OnPointerMoved: Starting drag mode for item: {_dragItem.BlendFileName}");
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
            // 处理 drop target - 只在目标改变时更新
            var dropItem = GetMouseOverItem(sender, e);
            if (dropItem != _previousDropItem)
            {
                if (dropItem != null)
                {
                    Console.WriteLine($"OnPointerMoved: Found new drop target: {dropItem.BlendFileName}");
                }
                UpdateDropTarget(dropItem);
            }
        }
        else
        {
            // 如果没达到阈值，清除所有 drop target 状态
            ClearDropTarget();
        }

        if (IsPointerOutsideListBox(e) || _dragItem == null)
        {
            Console.WriteLine("OnPointerMoved: Pointer outside listbox or dragItem is null, setting cursor to No");
            AssociatedObject.Cursor = new Cursor(StandardCursorType.No);
            return;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem == null) return;

        // 只有在真正拖拽状态下才执行排序操作
        if (!_isDragging)
        {
            ResetDragState();
            return;
        }

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

        Console.WriteLine($"IsClickOnDragHandle: Click position: {point}, found {visuals.Count} visual elements");

        // 检查是否点击在Tag为"DragHandle"的Border上或其子元素上
        foreach (var visual in visuals)
        {
            Console.WriteLine($"IsClickOnDragHandle: Checking visual element: {visual.GetType().Name}");
            
            // 检查是否是Border控件且Tag为"DragHandle"
            if (visual is Border border && border.Tag?.ToString() == "DragHandle")
            {
                Console.WriteLine("IsClickOnDragHandle: Directly clicking on drag handle border");
                return true;
            }
            
            // 检查是否点击在拖拽手柄Border的子元素上（比如图标）
            var dragHandleBorder = visual.GetLogicalAncestors().OfType<Border>()
                .FirstOrDefault(b => b.Tag?.ToString() == "DragHandle");
            if (dragHandleBorder != null)
            {
                Console.WriteLine("IsClickOnDragHandle: Clicking on child element of drag handle border");
                return true;
            }
        }

        Console.WriteLine("IsClickOnDragHandle: No drag handle found");
        return false;
    }

    private RenderTaskViewModel? GetDragItemFromHandle(PointerEventArgs e)
    {
        var point = e.GetPosition((Visual)AssociatedObject);
        var visuals = AssociatedObject.GetVisualsAt(point).ToList();

        Console.WriteLine($"GetDragItemFromHandle: Starting to find drag item, click position: {point}");

        // 查找拖拽手柄
        foreach (var visual in visuals)
        {
            Console.WriteLine($"GetDragItemFromHandle: Checking visual element: {visual.GetType().Name}");
            
            // 直接点击在拖拽手柄Border上
            if (visual is Border border && border.Tag?.ToString() == "DragHandle")
            {
                Console.WriteLine("GetDragItemFromHandle: Directly clicking on drag handle border");
                // 从拖拽手柄向上查找对应的 ListBoxItem
                var listBoxItem = border.GetLogicalAncestors().OfType<ListBoxItem>().FirstOrDefault();
                if (listBoxItem != null)
                {
                    var dataContext = listBoxItem.DataContext as RenderTaskViewModel;
                    Console.WriteLine($"GetDragItemFromHandle: Found ListBoxItem, DataContext: {dataContext?.BlendFileName ?? "null"}");
                    return dataContext;
                }
            }
            
            // 点击在拖拽手柄Border的子元素上（比如图标）
            var dragHandleBorder = visual.GetLogicalAncestors().OfType<Border>()
                .FirstOrDefault(b => b.Tag?.ToString() == "DragHandle");
            if (dragHandleBorder != null)
            {
                Console.WriteLine("GetDragItemFromHandle: Clicking on child element of drag handle border");
                // 从拖拽手柄向上查找对应的 ListBoxItem
                var listBoxItem = dragHandleBorder.GetLogicalAncestors().OfType<ListBoxItem>().FirstOrDefault();
                if (listBoxItem != null)
                {
                    var dataContext = listBoxItem.DataContext as RenderTaskViewModel;
                    Console.WriteLine($"GetDragItemFromHandle: Found ListBoxItem, DataContext: {dataContext?.BlendFileName ?? "null"}");
                    return dataContext;
                }
            }
        }

        Console.WriteLine("GetDragItemFromHandle: No drag item found");
        return null;
    }

    private RenderTaskViewModel? GetMouseOverItem(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition((Visual)sender);
        var visuals = ((Visual)sender).GetVisualsAt(point).ToList();

        Console.WriteLine($"GetMouseOverItem: Checking point: {point}, _dragItem: {_dragItem?.BlendFileName ?? "null"}");

        // 查找ListBoxItem，支持整个item区域作为drop目标
        foreach (var visual in visuals)
        {
            var listBoxItem = visual.GetLogicalAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (listBoxItem != null)
            {
                var dataContext = listBoxItem.DataContext as RenderTaskViewModel;
                Console.WriteLine($"GetMouseOverItem: Found ListBoxItem with DataContext: {dataContext?.BlendFileName ?? "null"}");
                
                if (dataContext != null && dataContext != _dragItem) // 排除拖拽项目本身
                {
                    Console.WriteLine($"GetMouseOverItem: Found valid drop target via visual tree: {dataContext.BlendFileName}");
                    return dataContext;
                }
                else if (dataContext == _dragItem)
                {
                    Console.WriteLine($"GetMouseOverItem: Skipping drag item itself: {dataContext.BlendFileName}");
                }
            }
        }

        // 如果通过视觉树找不到，尝试通过位置计算找到最接近的项目
        var closestItem = GetClosestItemByPosition(point);
        if (closestItem != null)
        {
            Console.WriteLine($"GetMouseOverItem: Found drop target via position: {closestItem.BlendFileName}");
        }
        else
        {
            Console.WriteLine($"GetMouseOverItem: No valid drop target found");
        }
        return closestItem;
    }

    private RenderTaskViewModel? GetClosestItemByPosition(Point point)
    {
        if (AssociatedObject == null) return null;

        var closestItem = (RenderTaskViewModel?)null;
        var closestDistance = double.MaxValue;

        Console.WriteLine($"GetClosestItemByPosition: Looking for closest item at point: {point}, dragItem: {_dragItem?.BlendFileName ?? "null"}");

        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            var dataContext = listBoxItem.DataContext as RenderTaskViewModel;
            if (dataContext != null && dataContext != _dragItem)
            {
                var itemBounds = listBoxItem.Bounds;
                var itemCenter = new Point(itemBounds.X + itemBounds.Width / 2, itemBounds.Y + itemBounds.Height / 2);
                
                var distance = Math.Sqrt(Math.Pow(point.X - itemCenter.X, 2) + Math.Pow(point.Y - itemCenter.Y, 2));
                
                Console.WriteLine($"GetClosestItemByPosition: Checking item: {dataContext.BlendFileName}, distance: {distance:F2}");
                
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestItem = dataContext;
                }
            }
            else if (dataContext == _dragItem)
            {
                Console.WriteLine($"GetClosestItemByPosition: Skipping drag item: {dataContext.BlendFileName}");
            }
        }

        Console.WriteLine($"GetClosestItemByPosition: Closest item: {closestItem?.BlendFileName ?? "null"}, distance: {closestDistance:F2}");
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
        return distance >= 10; // 恢复到合理的阈值，避免单击触发拖拽
    }

    private void SetDropTargetVisual(RenderTaskViewModel item, bool isDropTarget)
    {
        if (AssociatedObject == null) return;
        
        Console.WriteLine($"SetDropTargetVisual: Called for {item.BlendFileName}, isDropTarget: {isDropTarget}");
        
        // 查找对应的 ListBoxItem
        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            if (listBoxItem.DataContext == item)
            {
                Console.WriteLine($"SetDropTargetVisual: Found ListBoxItem for {item.BlendFileName}, setting isDropTarget: {isDropTarget}");
                DropTargetBehavior.SetIsDropTarget(listBoxItem, isDropTarget);
                break;
            }
        }
    }

    private void SetDragTargetVisual(RenderTaskViewModel item, bool isDragTarget)
    {
        if (AssociatedObject == null) return;
        
        Console.WriteLine($"SetDragTargetVisual: Called for {item.BlendFileName}, isDragTarget: {isDragTarget}");
        
        // 查找对应的 ListBoxItem
        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            if (listBoxItem.DataContext == item)
            {
                Console.WriteLine($"SetDragTargetVisual: Found ListBoxItem for {item.BlendFileName}, setting isDragTarget: {isDragTarget}");
                DropTargetBehavior.SetIsDragTarget(listBoxItem, isDragTarget);
                
                // 如果是拖拽目标，添加跟随鼠标的变换
                if (isDragTarget)
                {
                    // 设置初始变换
                    listBoxItem.RenderTransform = new TranslateTransform(0, 0);
                    Console.WriteLine($"SetDragTargetVisual: Set initial transform for {item.BlendFileName}");
                }
                else
                {
                    // 清除变换
                    listBoxItem.RenderTransform = null;
                    Console.WriteLine($"SetDragTargetVisual: Cleared transform for {item.BlendFileName}");
                }
                break;
            }
        }
    }

    private void UpdateDragItemTransform(PointerEventArgs e)
    {
        if (_dragItem == null || AssociatedObject == null) return;

        Console.WriteLine($"UpdateDragItemTransform: Called - _dragItem: {_dragItem.BlendFileName}, AssociatedObject: {AssociatedObject != null}");

        // 查找对应的 ListBoxItem
        foreach (var listBoxItem in AssociatedObject.GetLogicalDescendants().OfType<ListBoxItem>())
        {
            if (listBoxItem.DataContext == _dragItem)
            {
                Console.WriteLine($"UpdateDragItemTransform: Updating transform for {_dragItem.BlendFileName}");
                
                // 获取当前鼠标在 ListBox 中的位置
                var currentMousePosition = e.GetPosition(AssociatedObject);
                
                // 计算鼠标移动的偏移量
                var deltaX = currentMousePosition.X - _dragItemStartPosition.X;
                var deltaY = currentMousePosition.Y - _dragItemStartPosition.Y;
                
                Console.WriteLine($"UpdateDragItemTransform: Mouse position: {currentMousePosition.X}, {currentMousePosition.Y}, Start position: {_dragItemStartPosition.X}, {_dragItemStartPosition.Y}, Delta: ({deltaX}, {deltaY})");
                
                // 确保变换对象存在
                if (listBoxItem.RenderTransform is not TranslateTransform)
                {
                    listBoxItem.RenderTransform = new TranslateTransform(0, 0);
                    Console.WriteLine($"UpdateDragItemTransform: Created new TranslateTransform for {_dragItem.BlendFileName}");
                }
                
                // 应用变换
                if (listBoxItem.RenderTransform is TranslateTransform translateTransform)
                {
                    translateTransform.X = deltaX;
                    translateTransform.Y = deltaY;
                    Console.WriteLine($"UpdateDragItemTransform: Applied transform: ({deltaX}, {deltaY})");
                }
                break;
            }
        }
    }

    private void UpdateDropTarget(RenderTaskViewModel? dropItem)
    {
        Console.WriteLine($"UpdateDropTarget: Called with dropItem: {dropItem?.BlendFileName ?? "null"}, _dragItem: {_dragItem?.BlendFileName ?? "null"}");
        
        if (dropItem != null && dropItem != _dragItem)
        {
            Console.WriteLine($"UpdateDropTarget: Valid drop target found: {dropItem.BlendFileName}");
            
            // 如果找到了新的 drop target
            if (_previousDropItem != null && _previousDropItem != dropItem) 
            {
                Console.WriteLine($"UpdateDropTarget: Clearing previous drop target: {_previousDropItem.BlendFileName}");
                // 清除之前的 drop target
                _previousDropItem.IsDropTarget = false;
                SetDropTargetVisual(_previousDropItem, false);
            }

            // 设置新的 drop target
            Console.WriteLine($"UpdateDropTarget: Setting new drop target: {dropItem.BlendFileName}");
            dropItem.IsDropTarget = true;
            SetDropTargetVisual(dropItem, true);
            _previousDropItem = dropItem;
        }
        else
        {
            Console.WriteLine($"UpdateDropTarget: No valid drop target, clearing current state");
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