using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace BlenderSuite.RenderQueue.Behaviors;

/// <summary>
/// Behavior that handles file drop operations for <see cref="ContentControl"/>.
/// </summary>
public sealed class ContentControlFilesDropBehavior : DropBehaviorBase
{
    /// <summary>
    /// Identifies the <seealso cref="ContentDuringDrag"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<object?> ContentDuringDragProperty = 
        AvaloniaProperty.Register<ContentControlFilesDropBehavior, object?>(nameof(ContentDuringDrag));

    /// <summary>
    /// Identifies the <seealso cref="BackgroundDuringDrag"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BackgroundDuringDragProperty = 
        AvaloniaProperty.Register<ContentControlFilesDropBehavior, IBrush?>(nameof(BackgroundDuringDrag));

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentControlFilesDropBehavior"/> class.
    /// </summary>
    public ContentControlFilesDropBehavior()
    {
        PassEventArgsToCommand = true; // we need this to correctly pass the parameter on drop
        Handler = new FilesDropHandler(ExecuteCommandAdapter,
            () => ContentDuringDrag,
            () => BackgroundDuringDrag);
    }

    private void ExecuteCommandAdapter(object? parameter)
    {
        if (parameter is DragEventArgs dragEventArgs)
        {
            ExecuteCommand(dragEventArgs);
        }
        else if (parameter is IEnumerable<string> filePaths)
        {
            if (Command?.CanExecute(filePaths) == true)
            {
                Command.Execute(filePaths);
            }
        }
    }

    /// <summary>
    /// If sender is ContentControl - this content will be set during drag over
    /// </summary>
    public object? ContentDuringDrag
    {
        get => GetValue(ContentDuringDragProperty);
        set => SetValue(ContentDuringDragProperty, value);
    }

    /// <summary>
    /// If sender is ContentControl - this background will be set during drag over
    /// </summary>
    public IBrush? BackgroundDuringDrag
    {
        get => GetValue(BackgroundDuringDragProperty);
        set => SetValue(BackgroundDuringDragProperty, value);
    }

    private sealed class FilesDropHandler(
        Action<object?> execute,
        Func<object?> getContentDuringDrag,
        Func<IBrush?> getBackgroundDuringDrag) : DropHandlerBase
    {
        public override void Enter(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
        {
            base.Enter(sender, e, sourceContext, targetContext);

            if (e.DragEffects == DragDropEffects.None || sender is not ContentControl contentControl)
            {
                return;
            }

            if (getContentDuringDrag() is { } contentDuringDrag)
            {
                contentControl.SetCurrentValue(ContentControl.ContentProperty, contentDuringDrag);
            }

            if (getBackgroundDuringDrag() is { } backgroundDuringDrag)
            {
                contentControl.SetCurrentValue(TemplatedControl.BackgroundProperty, backgroundDuringDrag);
            }
        }

        public override void Leave(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
        {
            base.Leave(sender, e, sourceContext, targetContext);
            
            ClearDragValues(sender);
        }

        public override void Drop(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
        {
            base.Drop(sender, e, sourceContext, targetContext);
            
            ClearDragValues(sender);
        }

        public override bool Validate(object? sender,
            DragEventArgs e,
            object? sourceContext,
            object? targetContext,
            object? state)
        {
            return DropFileTransferHelper.ContainsFileDropData(e);
        }

        public override bool Execute(object? sender,
            DragEventArgs e,
            object? sourceContext,
            object? targetContext,
            object? state)
        {
            var filePaths = DropFileTransferHelper.ExtractFilePaths(e);
            if (filePaths.Count > 0)
            {
                execute(filePaths);
                return true;
            }

            return false;
        }

        public override void Cancel(object? sender, RoutedEventArgs e)
        {
            ClearDragValues(sender);
        }

        private static void ClearDragValues(object? sender)
        {
            if (sender is not ContentControl contentControl)
            {
                return;
            }

            contentControl.ClearValue(ContentControl.ContentProperty);
            contentControl.ClearValue(TemplatedControl.BackgroundProperty);
        }
    }
}
