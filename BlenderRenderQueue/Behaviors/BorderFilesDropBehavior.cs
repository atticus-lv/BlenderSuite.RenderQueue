using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Xaml.Interactivity;

namespace BlenderRenderQueue.Behaviors;

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
        else if (parameter is IEnumerable<IStorageItem> files)
        {
            if (Command?.CanExecute(files) == true)
            {
                Command.Execute(files);
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
            // 检查是否包含文件数据
            return e.Data.Contains(DataFormats.Files) || 
                   e.Data.Contains(DataFormats.FileNames) ||
                   e.Data.Contains(DataFormats.Text);
        }

        public override bool Execute(object? sender,
            DragEventArgs e,
            object? sourceContext,
            object? targetContext,
            object? state)
        {
            // 尝试获取文件
            var files = e.Data.GetFiles();
            if (files is not null)
            {
                execute(files);
                
                // 强制触发 Drop 事件来清理边框颜色
                Drop(sender, e, sourceContext, targetContext);
                
                return true;
            }

            // 如果 GetFiles() 返回 null，尝试获取文件名
            var fileNames = e.Data.Get(DataFormats.FileNames);
            if (fileNames is string[] filePaths)
            {
                // 将文件路径转换为 IStorageItem
                var storageFiles = filePaths
                    .Where(File.Exists)
                    .Select(path => new FileInfo(path))
                    .ToList();
                
                if (storageFiles.Any())
                {
                    execute(storageFiles);
                    return true;
                }
            }

            // 尝试从文本格式获取文件路径
            var text = e.Data.Get(DataFormats.Text);
            if (text is string textData && !string.IsNullOrEmpty(textData))
            {
                var lines = textData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var validFiles = lines
                    .Where(line => File.Exists(line.Trim()))
                    .Select(path => new FileInfo(path.Trim()))
                    .ToList();
                
                if (validFiles.Any())
                {
                    execute(validFiles);
                    return true;
                }
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
