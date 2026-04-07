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
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace BlenderRenderQueue.Behaviors;

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
    private bool _captured;
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
            
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, PointerReleased, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, PointerPressed, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, PointerMoved, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerCaptureLostEvent, PointerCaptureLost, RoutingStrategies.Tunnel);
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
        if (properties.IsLeftButtonPressed 
            && AssociatedObject?.Parent is ItemsControl itemsControl)
        {
            // Check if the click is on the drag handle
            if (!IsClickOnDragHandle(e))
            {
                return;
            }

            _enableDrag = true;
            _dragStarted = false;
            _start = e.GetPosition(itemsControl);
            _draggedIndex = -1;
            _targetIndex = -1;
            _itemsControl = itemsControl;
            _draggedContainer = AssociatedObject;

            if (_draggedContainer is not null)
            {
                SetDraggingPseudoClasses(_draggedContainer, true);
            }

            AddTransforms(_itemsControl);

            _captured = true;
        }
    }

    private bool IsClickOnDragHandle(PointerEventArgs e)
    {
        if (AssociatedObject is null) return false;
        
        var point = e.GetPosition(AssociatedObject);
        var visuals = AssociatedObject.GetVisualsAt(point).ToList();

        // Check if click is on an element with the drag handle tag
        foreach (var visual in visuals)
        {
            if (visual is Control control && control.Tag?.ToString() == DragHandleTag)
            {
                _dragHandle = control;
                return true;
            }
        }

        return false;
    }

    private void PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_captured)
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Released();
            }

            _captured = false;
        }
    }

    private void PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        Released();
        _captured = false;
    }

    private void Released()
    {
        if (!_enableDrag)
        {
            return;
        }

        RemoveTransforms(_itemsControl);

        if (_itemsControl is not null)
        {
            foreach (var control in _itemsControl.GetRealizedContainers())
            {
                SetDraggingPseudoClasses(control, true);
            }
        }

        if (_dragStarted)
        {
            if (_draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
            {
                MoveDraggedItem(_itemsControl, _draggedIndex, _targetIndex);
            }
        }

        if (_itemsControl is not null)
        {
            foreach (var control in _itemsControl.GetRealizedContainers())
            {
                SetDraggingPseudoClasses(control, false);
            }
        }

        if (_draggedContainer is not null)
        {
            SetDraggingPseudoClasses(_draggedContainer, false);
        }

        _draggedIndex = -1;
        _targetIndex = -1;
        _enableDrag = false;
        _dragStarted = false;
        _itemsControl = null;
        _draggedContainer = null;
        _dragHandle = null;
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
        if (itemsControl?.ItemsSource is IList itemsSource)
        {
            var draggedItem = itemsSource[draggedIndex];
            itemsSource.RemoveAt(draggedIndex);
            itemsSource.Insert(targetIndex, draggedItem);

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

    private void PointerMoved(object? sender, PointerEventArgs e)
    {
        var properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (_captured
            && properties.IsLeftButtonPressed)
        {
            if (_itemsControl?.Items is null || _draggedContainer?.RenderTransform is null || !_enableDrag)
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
                    }
                    else
                    {
                        return;
                    }
                }
            }

            if (orientation == Orientation.Horizontal)
            {
                SetTranslateTransform(_draggedContainer, delta, 0);
            }
            else
            {
                SetTranslateTransform(_draggedContainer, 0, delta);
            }

            _draggedIndex = _itemsControl.IndexFromContainer(_draggedContainer);
            _targetIndex = -1;

            var draggedBounds = _draggedContainer.Bounds;

            var draggedStart = orientation == Orientation.Horizontal ? draggedBounds.X : draggedBounds.Y;

            var draggedDeltaStart = orientation == Orientation.Horizontal
                ? draggedBounds.X + delta
                : draggedBounds.Y + delta;

            var draggedDeltaEnd = orientation == Orientation.Horizontal
                ? draggedBounds.X + delta + draggedBounds.Width
                : draggedBounds.Y + delta + draggedBounds.Height;

            var i = 0;

            foreach (var _ in _itemsControl.Items)
            {
                var targetContainer = _itemsControl.ContainerFromIndex(i);
                if (targetContainer?.RenderTransform is null || ReferenceEquals(targetContainer, _draggedContainer))
                {
                    i++;
                    continue;
                }

                var targetBounds = targetContainer.Bounds;

                var targetStart = orientation == Orientation.Horizontal ? targetBounds.X : targetBounds.Y;

                var targetMid = orientation == Orientation.Horizontal
                    ? targetBounds.X + targetBounds.Width / 2
                    : targetBounds.Y + targetBounds.Height / 2;

                var targetIndex = _itemsControl.IndexFromContainer(targetContainer);

                if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
                {
                    if (orientation == Orientation.Horizontal)
                    {
                        SetTranslateTransform(targetContainer, -draggedBounds.Width, 0);
                    }
                    else
                    {
                        SetTranslateTransform(targetContainer, 0, -draggedBounds.Height);
                    }

                    _targetIndex = _targetIndex == -1 ? targetIndex :
                        targetIndex > _targetIndex ? targetIndex : _targetIndex;
                }
                else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
                {
                    if (orientation == Orientation.Horizontal)
                    {
                        SetTranslateTransform(targetContainer, draggedBounds.Width, 0);
                    }
                    else
                    {
                        SetTranslateTransform(targetContainer, 0, draggedBounds.Height);
                    }

                    _targetIndex = _targetIndex == -1 ? targetIndex :
                        targetIndex < _targetIndex ? targetIndex : _targetIndex;
                }
                else
                {
                    if (orientation == Orientation.Horizontal)
                    {
                        SetTranslateTransform(targetContainer, 0, 0);
                    }
                    else
                    {
                        SetTranslateTransform(targetContainer, 0, 0);
                    }
                }

                i++;
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

    private void SetTranslateTransform(Control control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }
}
