using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;

namespace BlenderSuite.RenderQueue.Behaviors;

/// <summary>
/// Behavior that handles file drop operations for <see cref="Border"/>.
/// </summary>
public sealed class BorderFilesDropBehavior : DropBehaviorBase
{
    /// <summary>
    /// Identifies the <seealso cref="BorderBrushDuringDrag"/> avalonia property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BorderBrushDuringDragProperty = 
        AvaloniaProperty.Register<BorderFilesDropBehavior, IBrush?>(nameof(BorderBrushDuringDrag));

    /// <summary>
    /// Initializes a new instance of the <see cref="BorderFilesDropBehavior"/> class.
    /// </summary>
    public BorderFilesDropBehavior()
    {
        PassEventArgsToCommand = true; // we need this to correctly pass the parameter on drop
        Handler = new BorderFilesDropHandler(ExecuteCommandAdapter,
            () => BorderBrushDuringDrag);
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
    /// Border brush that will be set during drag over
    /// </summary>
    public IBrush? BorderBrushDuringDrag
    {
        get => GetValue(BorderBrushDuringDragProperty);
        set => SetValue(BorderBrushDuringDragProperty, value);
    }

    private sealed class BorderFilesDropHandler(
        Action<object?> execute,
        Func<IBrush?> getBorderBrushDuringDrag) : DropHandlerBase
    {
        public override void Enter(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
        {
            base.Enter(sender, e, sourceContext, targetContext);

            if (e.DragEffects == DragDropEffects.None || sender is not Border border)
            {
                return;
            }

            if (getBorderBrushDuringDrag() is { } borderBrushDuringDrag)
            {
                border.SetCurrentValue(Border.BorderBrushProperty, borderBrushDuringDrag);
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

                // 强制触发 Drop 事件来清理边框颜色
                Drop(sender, e, sourceContext, targetContext);

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
            if (sender is not Border border)
            {
                return;
            }

            // 将边框颜色设置为透明，而不是清除值
            border.SetCurrentValue(Border.BorderBrushProperty, Brushes.Transparent);
        }
    }
}
