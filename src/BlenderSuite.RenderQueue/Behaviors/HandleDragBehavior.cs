using System;
using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace BlenderSuite.RenderQueue.Behaviors;

/// <summary>
/// Allows dragging items within an ItemsControl, but only when dragging from a specific handle element.
/// </summary>
public class HandleDragBehavior : StyledElementBehavior<Control>
{
    private bool _enableDrag;
    private bool _dragStarted;
    private Point _start;
    private int _draggedIndex;
    private int _targetIndex;
    private ItemsControl? _itemsControl;
    private Control? _draggedContainer;
    private Control? _captureTarget;
    private bool _captured;
    private bool _isReleasing;
    private IPointer? _capturedPointer;
    private Control? _dragHandle;

    /// <summary>
    /// Identifies the <see cref="Orientation"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<HandleDragBehavior, Orientation>(nameof(Orientation));

    /// <summary>
    /// Identifies the <see cref="HorizontalDragThreshold"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<double> HorizontalDragThresholdProperty =
        AvaloniaProperty.Register<HandleDragBehavior, double>(nameof(HorizontalDragThreshold), 3);

    /// <summary>
    /// Identifies the <see cref="VerticalDragThreshold"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<double> VerticalDragThresholdProperty =
        AvaloniaProperty.Register<HandleDragBehavior, double>(nameof(VerticalDragThreshold), 3);

    /// <summary>
    /// Identifies the <see cref="DragHandleTag"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<string> DragHandleTagProperty =
        AvaloniaProperty.Register<HandleDragBehavior, string>(nameof(DragHandleTag), "DragHandle");

    /// <summary>
    /// Gets or sets the orientation of the drag operation.
    /// </summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal drag threshold in pixels.
    /// </summary>
    public double HorizontalDragThreshold
    {
        get => GetValue(HorizontalDragThresholdProperty);
        set => SetValue(HorizontalDragThresholdProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical drag threshold in pixels.
    /// </summary>
    public double VerticalDragThreshold
    {
        get => GetValue(VerticalDragThresholdProperty);
        set => SetValue(VerticalDragThresholdProperty, value);
    }

    /// <summary>
    /// Gets or sets the tag that identifies the drag handle element.
    /// </summary>
    public string DragHandleTag
    {
        get => GetValue(DragHandleTagProperty);
        set => SetValue(DragHandleTagProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree()
    {
        AttachEventHandlers();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree()
    {
        DetachEventHandlers();
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        // 确保在附加时也设置事件处理器
        if (AssociatedObject is not null && AssociatedObject.IsAttachedToVisualTree())
        {
            AttachEventHandlers();
        }
    }

    private void AttachEventHandlers()
    {
        if (AssociatedObject is not null)
        {
            // 先移除可能存在的处理器，避免重复添加
            DetachEventHandlers();

            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            AssociatedObject.AddHandler(InputElement.PointerCaptureLostEvent, PointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    private void DetachEventHandlers()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, PointerReleased);
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, PointerPressed);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, PointerMoved);
            AssociatedObject.RemoveHandler(InputElement.PointerCaptureLostEvent, PointerCaptureLost);
        }
    }

    private void PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (properties.IsLeftButtonPressed && AssociatedObject is not null)
        {
            if (_captured || _enableDrag)
            {
                Released();
            }

            var itemsControl = AssociatedObject as ItemsControl ?? ItemsControl.ItemsControlFromItemContainer(AssociatedObject);
            if (itemsControl is null)
            {
                return;
            }

            var draggedContainer = AssociatedObject is ItemsControl
                ? FindItemContainer(e.Source as Visual, itemsControl)
                : AssociatedObject;
            if (draggedContainer is null)
            {
                return;
            }

            // Check if the click is on the drag handle
            if (!IsClickOnDragHandle(e, draggedContainer))
            {
                return;
            }

            _enableDrag = true;
            _dragStarted = false;
            _start = e.GetPosition(itemsControl);
            _itemsControl = itemsControl;
            _draggedContainer = draggedContainer;
            _draggedIndex = itemsControl.IndexFromContainer(_draggedContainer);
            _targetIndex = _draggedIndex;

            if (_draggedIndex < 0)
            {
                ResetDragState();
                return;
            }

            ClearDraggingPseudoClasses(_itemsControl);
            AddTransforms(_itemsControl);

            _capturedPointer = e.Pointer;
            _captureTarget = AssociatedObject;
            _capturedPointer.Capture(_captureTarget);
            if (_capturedPointer.Captured != _captureTarget)
            {
                RemoveTransforms(_itemsControl);
                ResetDragState();
                return;
            }

            _captured = true;
            e.Handled = true;
        }
    }

    private static Control? FindItemContainer(Visual? visual, ItemsControl itemsControl)
    {
        if (visual is null)
        {
            return null;
        }

        if (visual is Control control && itemsControl.IndexFromContainer(control) >= 0)
        {
            return control;
        }

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is Control ancestorControl && itemsControl.IndexFromContainer(ancestorControl) >= 0)
            {
                return ancestorControl;
            }
        }

        return null;
    }

    private bool IsClickOnDragHandle(PointerEventArgs e, Control itemContainer)
    {
        if (AssociatedObject is null)
        {
            return false;
        }

        _dragHandle = null;

        if (TryFindDragHandle(e.Source as Visual, itemContainer))
        {
            return true;
        }

        var point = e.GetPosition(itemContainer);

        foreach (var control in itemContainer.GetVisualDescendants().OfType<Control>())
        {
            if (IsDragHandle(control) && IsPointInsideControl(control, itemContainer, point))
            {
                return true;
            }
        }

        var visuals = itemContainer.GetVisualsAt(point).ToList();

        foreach (var visual in visuals)
        {
            if (TryFindDragHandle(visual, itemContainer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideControl(Control control, Control itemContainer, Point pointRelativeToItemContainer)
    {
        var topLeft = control.TranslatePoint(default, itemContainer);
        if (topLeft is null)
        {
            return false;
        }

        return new Rect(topLeft.Value, control.Bounds.Size).Contains(pointRelativeToItemContainer);
    }

    private bool TryFindDragHandle(Visual? visual, Control itemContainer)
    {
        if (visual is null)
        {
            return false;
        }

        if (visual is Control control && IsDragHandle(control))
        {
            return true;
        }

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is not Control ancestorControl)
            {
                continue;
            }

            if (ReferenceEquals(ancestorControl, itemContainer))
            {
                break;
            }

            if (IsDragHandle(ancestorControl))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsDragHandle(Control control)
    {
        if (!string.Equals(control.Tag?.ToString(), DragHandleTag, StringComparison.Ordinal))
        {
            return false;
        }

        _dragHandle = control;
        return true;
    }

    private void PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_captured && ReferenceEquals(e.Pointer, _capturedPointer))
        {
            Released();
            e.Handled = true;
        }
    }

    private void PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Pointer, _capturedPointer))
        {
            Released();
            e.Handled = true;
        }
    }

    private void Released()
    {
        if (_isReleasing)
        {
            return;
        }

        if (!_enableDrag && !_captured)
        {
            ReleasePointerCapture();
            return;
        }

        _isReleasing = true;
        var shouldMove = false;
        ItemsControl? moveItemsControl = null;
        var moveDraggedIndex = -1;
        var moveTargetIndex = -1;

        try
        {
            var itemsControl = _itemsControl;
            var draggedIndex = _draggedIndex;
            var targetIndex = _targetIndex;
            shouldMove = _dragStarted && IsValidMove(itemsControl, draggedIndex, targetIndex);

            try
            {
                RemoveTransforms(itemsControl);
                ClearDraggingPseudoClasses(itemsControl);

                if (shouldMove)
                {
                    moveItemsControl = itemsControl;
                    moveDraggedIndex = draggedIndex;
                    moveTargetIndex = targetIndex;
                }
            }
            finally
            {
                ReleasePointerCapture();
            }
        }
        finally
        {
            ClearDraggingPseudoClasses(_itemsControl);
            ResetDragState();
            _isReleasing = false;
        }

        if (shouldMove)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    MoveDraggedItem(moveItemsControl, moveDraggedIndex, moveTargetIndex);
                },
                DispatcherPriority.Background);
        }
    }

    private void AddTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.Items is null)
        {
            return;
        }

        var i = 0;

        foreach (var _ in itemsControl.Items)
        {
            var container = itemsControl.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private void RemoveTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.Items is null)
        {
            return;
        }

        var i = 0;

        foreach (var _ in itemsControl.Items)
        {
            var container = itemsControl.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private void MoveDraggedItem(ItemsControl? itemsControl, int draggedIndex, int targetIndex)
    {
        if (!IsValidMove(itemsControl, draggedIndex, targetIndex))
        {
            return;
        }

        if (itemsControl?.ItemsSource is IList itemsSource)
        {
            if (!TryMoveItemsSource(itemsSource, draggedIndex, targetIndex))
            {
                var draggedItem = itemsSource[draggedIndex];
                itemsSource.RemoveAt(draggedIndex);
                itemsSource.Insert(targetIndex, draggedItem);
            }

            if (itemsControl is SelectingItemsControl selectingItemsControl)
            {
                selectingItemsControl.SelectedIndex = targetIndex;
            }
        }
        else
        {
            if (itemsControl?.Items is {IsReadOnly: false} itemCollection)
            {
                var draggedItem = itemCollection[draggedIndex];
                itemCollection.RemoveAt(draggedIndex);
                itemCollection.Insert(targetIndex, draggedItem);

                if (itemsControl is SelectingItemsControl selectingItemsControl)
                {
                    selectingItemsControl.SelectedIndex = targetIndex;
                }
            }
        }
    }

    private static bool TryMoveItemsSource(IList itemsSource, int draggedIndex, int targetIndex)
    {
        var moveMethod = itemsSource.GetType().GetMethod("Move", [typeof(int), typeof(int)]);
        if (moveMethod is null)
        {
            return false;
        }

        moveMethod.Invoke(itemsSource, [draggedIndex, targetIndex]);
        return true;
    }

    private static bool IsValidMove(ItemsControl? itemsControl, int draggedIndex, int targetIndex)
    {
        var itemCount = GetItemCount(itemsControl);
        return itemCount > 0 &&
               draggedIndex >= 0 &&
               targetIndex >= 0 &&
               draggedIndex < itemCount &&
               targetIndex < itemCount &&
               draggedIndex != targetIndex;
    }

    private void PointerMoved(object? sender, PointerEventArgs e)
    {
        if (AssociatedObject is null)
        {
            return;
        }

        if (_captured && !ReferenceEquals(e.Pointer, _capturedPointer))
        {
            return;
        }

        if (_captured)
        {
            if (_itemsControl?.Items is null || _draggedContainer is null || !_enableDrag)
            {
                return;
            }

            var orientation = Orientation;
            var position = e.GetPosition(_itemsControl);
            var delta = orientation == Orientation.Horizontal ? position.X - _start.X : position.Y - _start.Y;

            if (!_dragStarted)
            {
                var diff = _start - position;
                var horizontalDragThreshold = HorizontalDragThreshold;
                var verticalDragThreshold = VerticalDragThreshold;

                if (orientation == Orientation.Horizontal)
                {
                    if (Math.Abs(diff.X) > horizontalDragThreshold)
                    {
                        _dragStarted = true;
                        SetDraggingPseudoClasses(_draggedContainer, true);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    if (Math.Abs(diff.Y) > verticalDragThreshold)
                    {
                        _dragStarted = true;
                        SetDraggingPseudoClasses(_draggedContainer, true);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            if (_draggedIndex < 0)
            {
                _draggedIndex = _itemsControl.IndexFromContainer(_draggedContainer);
                _targetIndex = _draggedIndex;
            }

            if (_draggedIndex < 0)
            {
                return;
            }

            if (orientation == Orientation.Horizontal)
            {
                SetTranslateTransform(_draggedContainer, delta, 0);
            }
            else
            {
                SetTranslateTransform(_draggedContainer, 0, delta);
            }

            _targetIndex = CalculateTargetIndex(_itemsControl, _draggedContainer, position, orientation);
            ApplySiblingTransforms(_itemsControl, _draggedContainer, _draggedIndex, _targetIndex, orientation);
            e.Handled = true;
        }
    }

    private static int CalculateTargetIndex(
        ItemsControl itemsControl,
        Control draggedContainer,
        Point pointerPosition,
        Orientation orientation)
    {
        var insertionIndex = GetItemCount(itemsControl);
        var draggedIndex = itemsControl.IndexFromContainer(draggedContainer);
        var pointerAxis = orientation == Orientation.Horizontal ? pointerPosition.X : pointerPosition.Y;

        for (var i = 0; i < GetItemCount(itemsControl); i++)
        {
            var targetContainer = itemsControl.ContainerFromIndex(i);
            if (targetContainer is null || ReferenceEquals(targetContainer, draggedContainer))
            {
                continue;
            }

            var bounds = targetContainer.Bounds;
            var midpoint = orientation == Orientation.Horizontal
                ? bounds.X + bounds.Width / 2
                : bounds.Y + bounds.Height / 2;

            if (pointerAxis < midpoint)
            {
                insertionIndex = i;
                break;
            }
        }

        var targetIndex = insertionIndex > draggedIndex ? insertionIndex - 1 : insertionIndex;
        return Math.Clamp(targetIndex, 0, Math.Max(0, GetItemCount(itemsControl) - 1));
    }

    private void ApplySiblingTransforms(
        ItemsControl itemsControl,
        Control draggedContainer,
        int draggedIndex,
        int targetIndex,
        Orientation orientation)
    {
        var draggedBounds = draggedContainer.Bounds;
        var offset = orientation == Orientation.Horizontal ? draggedBounds.Width : draggedBounds.Height;

        for (var i = 0; i < GetItemCount(itemsControl); i++)
        {
            var targetContainer = itemsControl.ContainerFromIndex(i);
            if (targetContainer is null || ReferenceEquals(targetContainer, draggedContainer))
            {
                continue;
            }

            var shift = 0.0;
            if (targetIndex > draggedIndex && i > draggedIndex && i <= targetIndex)
            {
                shift = -offset;
            }
            else if (targetIndex < draggedIndex && i >= targetIndex && i < draggedIndex)
            {
                shift = offset;
            }

            if (orientation == Orientation.Horizontal)
            {
                SetTranslateTransform(targetContainer, shift, 0);
            }
            else
            {
                SetTranslateTransform(targetContainer, 0, shift);
            }
        }
    }

    private void SetDraggingPseudoClasses(Control control, bool isDragging)
    {
        if (isDragging)
        {
            ((IPseudoClasses)control.Classes).Add(":dragging");
        }
        else
        {
            ((IPseudoClasses)control.Classes).Remove(":dragging");
        }
    }

    private void ClearDraggingPseudoClasses(ItemsControl? itemsControl)
    {
        if (itemsControl is null)
        {
            return;
        }

        foreach (var control in itemsControl.GetRealizedContainers())
        {
            SetDraggingPseudoClasses(control, false);
        }
    }

    private void SetTranslateTransform(Control control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }

    private void ReleasePointerCapture()
    {
        var capturedPointer = _capturedPointer;
        if (capturedPointer is not null && capturedPointer.Captured == _captureTarget)
        {
            capturedPointer.Capture(null);
        }

        _capturedPointer = null;
        _captureTarget = null;
    }

    private void ResetDragState()
    {
        _draggedIndex = -1;
        _targetIndex = -1;
        _enableDrag = false;
        _dragStarted = false;
        _itemsControl = null;
        _draggedContainer = null;
        _captureTarget = null;
        _dragHandle = null;
        _captured = false;
    }

    private static int GetItemCount(ItemsControl? itemsControl)
    {
        return itemsControl?.Items?.Count ?? 0;
    }
}
