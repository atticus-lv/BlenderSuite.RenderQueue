using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BlenderRenderQueue.Behaviors;

/// <summary>
/// Base class for drop handlers.
/// </summary>
public abstract class DropHandlerBase
{
    /// <summary>
    /// Called when drag enters the drop target.
    /// </summary>
    public virtual void Enter(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
    }

    /// <summary>
    /// Called when drag leaves the drop target.
    /// </summary>
    public virtual void Leave(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
    }

    /// <summary>
    /// Called when files are dropped.
    /// </summary>
    public virtual void Drop(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
    }

    /// <summary>
    /// Called when the drop operation is cancelled.
    /// </summary>
    public virtual void Cancel(object? sender, RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Validates whether the drop operation is allowed.
    /// </summary>
    public abstract bool Validate(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state);

    /// <summary>
    /// Executes the drop operation.
    /// </summary>
    public abstract bool Execute(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state);
}

