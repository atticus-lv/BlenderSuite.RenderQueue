using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using BlenderSuite.RenderQueue.Behaviors;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Views;

public partial class FileDropView : UserControl
{
    public FileDropView()
    {
        InitializeComponent();
        
        // 在 Loaded 事件中设置命令
        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 查找拖放行为并设置命令
        var dropBehavior = this.FindBehavior<ContentControlFilesDropBehavior>();
        if (dropBehavior != null)
        {
            dropBehavior.Command = new RelayCommand<IEnumerable<IStorageItem>>(OnFilesDropped);
        }
    }

    private T? FindBehavior<T>() where T : class
    {
        return this.GetVisualDescendants()
            .OfType<Control>()
            .SelectMany(control => 
            {
                var behaviors = control.GetValue(Interaction.BehaviorsProperty);
                return behaviors?.Cast<object>() ?? Enumerable.Empty<object>();
            })
            .OfType<T>()
            .FirstOrDefault();
    }

    private async void OnFilesDropped(IEnumerable<IStorageItem> files)
    {
        try
        {
            var blendFiles = files
                .OfType<IStorageFile>()
                .Where(file => file.Name.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!blendFiles.Any())
            {
                ShowMessage("请拖拽 .blend 文件", "错误");
                return;
            }

            // 获取主窗口的 ViewModel
            var mainWindow = this.FindAncestorOfType<Window>();
            if (mainWindow?.DataContext is MainRenderViewModel mainViewModel)
            {
                var renderQueueViewModel = mainViewModel.RenderQueue;
                
                // 添加文件到渲染队列
                foreach (var file in blendFiles)
                {
                    var filePath = file.Path.LocalPath;
                    await AddBlendFileToQueue(renderQueueViewModel, filePath);
                }
                
                ShowMessage($"成功添加 {blendFiles.Count} 个文件到渲染队列", "成功");
            }
            else
            {
                ShowMessage("无法找到渲染队列", "错误");
            }
        }
        catch (Exception ex)
        {
            UnhandledExceptionGuard.WriteHandledException(
                ex,
                nameof(FileDropView),
                RenderLogScope.System,
                "拖拽添加文件事件处理失败。");
            ShowMessage($"添加文件时出错: {ex.Message}", "错误");
        }
    }

    private Task AddBlendFileToQueue(RenderQueueViewModel renderQueueViewModel, string filePath)
    {
        // 检查文件是否存在
        if (!File.Exists(filePath))
        {
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"文件不存在: {filePath}", "FileDropView");
            return Task.CompletedTask;
        }

        renderQueueViewModel.AddDroppedFiles([filePath]);
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"已添加任务: {Path.GetFileName(filePath)}", "FileDropView");
        return Task.CompletedTask;
    }

    private void ShowMessage(string message, string title)
    {
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"{title}: {message}", "FileDropView");
        
        // 可以在这里添加实际的 UI 提示逻辑
        // 比如触发主窗口的 Toast 事件
        var mainWindow = this.FindAncestorOfType<Window>();
        if (mainWindow?.DataContext is MainRenderViewModel mainViewModel)
        {
            // 触发 Toast 事件 - 使用公共方法
            // mainViewModel.ShowToastMessage?.Invoke(title, message);
            // 暂时只输出到控制台
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Toast: {title} - {message}", "FileDropView");
        }
    }
}

// 简单的 RelayCommand 实现
public class RelayCommand<T> : System.Windows.Input.ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke((T)parameter!) ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute((T)parameter!);
    }
}
