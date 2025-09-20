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

    public ListBoxDragDropBehavior()
    {
        Console.WriteLine("[DragDrop] ListBoxDragDropBehavior created");
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is null) return;

        Console.WriteLine("[DragDrop] OnAttached - adding event handlers");
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
        Console.WriteLine("[DragDrop] OnPointerPressed called");
        _dragItem = GetMouseOverItem(sender, e);
        if (_dragItem == null)
        {
            Console.WriteLine("[DragDrop] No drag item found");
            return;
        }

        _startPoint = e.GetPosition(AssociatedObject.GetVisualRoot() as Visual);
        Console.WriteLine($"[DragDrop] Pointer pressed on: {_dragItem.BlendFileName}");
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem == null) return;

        var dropItem = GetMouseOverItem(sender, e);
        if (dropItem != null && dropItem != _dragItem)
        {
            if (_previousDropItem != null && _previousDropItem != dropItem) _previousDropItem.IsDropTarget = false;

            dropItem.IsDropTarget = true;
            _previousDropItem = dropItem;
        }
        else
        {
            if (_previousDropItem != null)
            {
                _previousDropItem.IsDropTarget = false;
                _previousDropItem = null;
            }
        }

        if (IsPointerOutsideListBox(e) || _dragItem == null)
        {
            AssociatedObject.Cursor = new Cursor(StandardCursorType.No);
            return;
        }


        var currentPosition = e.GetPosition(AssociatedObject.GetVisualRoot() as Visual);
        if (!IsDragThresholdExceeded(currentPosition)) return;

        if (!_isDragging) _isDragging = true;

        if (_isDragging)
        {
            var viewModel = AssociatedObject.DataContext as RenderQueueViewModel;
            AssociatedObject.Cursor = viewModel is { CanModifyTasks: false }
                ? new Cursor(StandardCursorType.No)
                : new Cursor(StandardCursorType.DragMove);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem == null) return;

        var dropItem = GetMouseOverItem(sender, e);
        Console.WriteLine(
            $"[DragDrop] Pointer released - DragItem: {_dragItem?.BlendFileName}, DropItem: {dropItem?.BlendFileName}");

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

            Console.WriteLine($"[DragDrop] Moving from index {dragIndex} to {dropIndex}");

            if (dragIndex >= 0 && dropIndex >= 0)
            {
                viewModel.RenderTasks.Move(dragIndex, dropIndex);
                Console.WriteLine("[DragDrop] Move completed");
            }
        }

        ResetDragState();
    }

    private RenderTaskViewModel? GetMouseOverItem(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition((Visual)sender);
        var visuals = ((Visual)sender).GetVisualsAt(point).ToList();

        Console.WriteLine($"[DragDrop] Found {visuals.Count} visuals at point");

        // 查找ListBoxItem，跳过Border等装饰元素
        foreach (var visual in visuals)
        {
            var listBoxItem = visual.GetLogicalAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (listBoxItem != null)
            {
                var dataContext = listBoxItem.DataContext as RenderTaskViewModel;
                if (dataContext != null)
                {
                    Console.WriteLine($"[DragDrop] GetMouseOverItem found: {dataContext.BlendFileName}");
                    return dataContext;
                }
            }
        }

        Console.WriteLine("[DragDrop] GetMouseOverItem found no item");
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
        return distance >= 10;
    }

    private void ResetDragState()
    {
        _dragItem = null;
        _isDragging = false;
        AssociatedObject.Cursor = Cursor.Default;
        if (_previousDropItem == null) return;
        _previousDropItem.IsDropTarget = false;
        _previousDropItem = null;
    }
}