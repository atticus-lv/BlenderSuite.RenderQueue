using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Xaml.Interactivity;

namespace BlenderSuite.RenderQueue.Behaviors;

/// <summary>
/// Base class for drop behaviors.
/// </summary>
public abstract class DropBehaviorBase : Behavior<Control>
{
    /// <summary>
    /// Identifies the <seealso cref="Command"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CommandProperty = 
        AvaloniaProperty.Register<DropBehaviorBase, ICommand?>(nameof(Command));

    /// <summary>
    /// Identifies the <seealso cref="PassEventArgsToCommand"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<bool> PassEventArgsToCommandProperty = 
        AvaloniaProperty.Register<DropBehaviorBase, bool>(nameof(PassEventArgsToCommand), true);

    /// <summary>
    /// Gets or sets the command to execute when files are dropped.
    /// </summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to pass the event args to the command.
    /// </summary>
    public bool PassEventArgsToCommand
    {
        get => GetValue(PassEventArgsToCommandProperty);
        set => SetValue(PassEventArgsToCommandProperty, value);
    }

    /// <summary>
    /// Gets the drop handler.
    /// </summary>
    protected DropHandlerBase Handler { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        
        if (AssociatedObject is not null)
        {
            AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
            AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
            AssociatedObject.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble);
        }
    }

    /// <inheritdoc />
    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
        {
            AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
            AssociatedObject.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }
        
        base.OnDetaching();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (AssociatedObject is null)
        {
            return;
        }

        var sourceContext = GetDataContext(sender);
        var targetContext = GetDataContext(AssociatedObject);
        var state = new object();

        if (Handler.Validate(AssociatedObject, e, sourceContext, targetContext, state))
        {
            e.DragEffects = e.DragEffects & (DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move);
            Handler.Enter(AssociatedObject, e, sourceContext, targetContext);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            Handler.Leave(AssociatedObject, e, sourceContext, targetContext);
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (AssociatedObject is null)
        {
            return;
        }

        var sourceContext = GetDataContext(sender);
        var targetContext = GetDataContext(AssociatedObject);
        var state = new object();

        if (Handler.Validate(AssociatedObject, e, sourceContext, targetContext, state))
        {
            Handler.Execute(AssociatedObject, e, sourceContext, targetContext, state);
        }

        Handler.Leave(AssociatedObject, e, sourceContext, targetContext);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (AssociatedObject is null)
        {
            return;
        }

        var sourceContext = GetDataContext(sender);
        var targetContext = GetDataContext(AssociatedObject);

        Handler.Leave(AssociatedObject, e, sourceContext, targetContext);
    }

    protected void ExecuteCommand(DragEventArgs e)
    {
        if (Command is null)
        {
            return;
        }

        var parameter = PassEventArgsToCommand ? (object)e : e.DataTransfer;
        
        if (Command.CanExecute(parameter))
        {
            Command.Execute(parameter);
        }
    }

    private static object? GetDataContext(object? sender)
    {
        return sender is Control control ? control.DataContext : null;
    }
}
