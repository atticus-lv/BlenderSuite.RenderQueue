using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services.BlenderService;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform.Storage;

namespace BlenderRenderQueue.ViewModels;

public partial class RenderQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<RenderTaskViewModel> _renderTasks = new();

    [ObservableProperty]
    private RenderTaskViewModel? _selectedTask;

    [ObservableProperty]
    private bool _isQueueRunning = false;

    [ObservableProperty]
    private int _activeTaskCount = 0;

    [ObservableProperty]
    private int _completedTaskCount = 0;

    [ObservableProperty]
    private int _failedTaskCount = 0;

    [ObservableProperty]
    private double _overallQueueProgress = 0.0;

    [ObservableProperty]
    private string _queueStatusText = "队列空闲";

    [ObservableProperty]
    private bool _autoStartNext = true; // 自动开始下一个任务

    // 内部状态
    private readonly List<Task> _runningTasks = new();
    private BlenderExeService? _blenderService;
    private readonly object _queueLock = new object();

    // 事件
    public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;

    public RenderQueueViewModel()
    {
        // 监听任务状态变化
        RenderTasks.CollectionChanged += (s, e) => UpdateQueueStatistics();
    }

    [RelayCommand]
    private async Task AddTask()
    {
        var blendFile = await SelectBlendFile();
        if (string.IsNullOrWhiteSpace(blendFile)) return;

        var task = new RenderTaskViewModel(blendFile, 1, 1, true);
        
        // 如果有Blender服务，自动加载文件属性
        if (_blenderService != null)
        {
            await task.LoadFilePropertiesAsync(_blenderService);
        }

        RenderTasks.Add(task);
        
        // 订阅任务事件
        SubscribeToTaskEvents(task);
        
        UpdateQueueStatistics();
    }

    [RelayCommand]
    private async Task AddMultipleTasks()
    {
        var blendFiles = await SelectMultipleBlendFiles();
        if (blendFiles == null || !blendFiles.Any()) return;

        foreach (var blendFile in blendFiles)
        {
            var task = new RenderTaskViewModel(blendFile, 1, 1, true);
            
            // 如果有Blender服务，自动加载文件属性
            if (_blenderService != null)
            {
                await task.LoadFilePropertiesAsync(_blenderService);
            }

            RenderTasks.Add(task);
            
            // 订阅任务事件
            SubscribeToTaskEvents(task);
        }
        
        UpdateQueueStatistics();
    }

    [RelayCommand]
    private void RemoveSelectedTask()
    {
        if (SelectedTask == null) return;

        // 如果任务正在运行，先停止
        if (SelectedTask.Status == RenderTaskStatus.Running)
        {
            SelectedTask.StopRender();
        }

        // 取消订阅事件
        UnsubscribeFromTaskEvents(SelectedTask);
        
        RenderTasks.Remove(SelectedTask);
        SelectedTask.Dispose();
        SelectedTask = null;
        
        UpdateQueueStatistics();
    }

    [RelayCommand]
    private void RemoveAllTasks()
    {
        // 停止所有运行中的任务
        foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
        {
            task.StopRender();
        }

        // 取消订阅所有事件并释放资源
        foreach (var task in RenderTasks)
        {
            UnsubscribeFromTaskEvents(task);
            task.Dispose();
        }

        RenderTasks.Clear();
        SelectedTask = null;
        UpdateQueueStatistics();
    }

    [RelayCommand]
    private void RemoveCompletedTasks()
    {
        var completedTasks = RenderTasks.Where(t => 
            t.Status == RenderTaskStatus.Completed || 
            t.Status == RenderTaskStatus.Failed || 
            t.Status == RenderTaskStatus.Cancelled).ToList();

        foreach (var task in completedTasks)
        {
            UnsubscribeFromTaskEvents(task);
            RenderTasks.Remove(task);
            task.Dispose();
        }
        
        UpdateQueueStatistics();
    }

    [RelayCommand]
    private async Task StartQueue()
    {
        if (IsQueueRunning) return;
        if (!RenderTasks.Any()) return;
        if (_blenderService == null)
        {
            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("需要先设置Blender路径"));
            return;
        }

        // 停止队列：重置所有任务状态，从头开始
        foreach (var task in RenderTasks)
        {
            if (task.Status == RenderTaskStatus.Running)
            {
                task.StopRender();
            }
            // 重置所有任务状态为等待中，从头开始
            task.Status = RenderTaskStatus.Pending;
            task.StatusText = "等待中";
            // 重置进度信息
            task.ResetProgress();
        }

        IsQueueRunning = true;
        QueueStatusText = "队列运行中";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("队列已启动"));

        // 启动第一个任务
        await StartNextAvailableTasks();
    }

    [RelayCommand]
    private void StopQueue()
    {
        if (!IsQueueRunning) return;

        // 停止所有运行中的任务
        foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
        {
            task.StopRender();
        }

        IsQueueRunning = false;
        QueueStatusText = "队列已停止";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("队列已停止"));
    }

    [RelayCommand]
    private void MoveTaskUp()
    {
        if (SelectedTask == null) return;
        
        var index = RenderTasks.IndexOf(SelectedTask);
        if (index > 0)
        {
            RenderTasks.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveTaskDown()
    {
        if (SelectedTask == null) return;
        
        var index = RenderTasks.IndexOf(SelectedTask);
        if (index < RenderTasks.Count - 1)
        {
            RenderTasks.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void MoveTaskToTop()
    {
        if (SelectedTask == null) return;
        
        var index = RenderTasks.IndexOf(SelectedTask);
        if (index > 0)
        {
            RenderTasks.Move(index, 0);
        }
    }

    [RelayCommand]
    private void MoveTaskToBottom()
    {
        if (SelectedTask == null) return;
        
        var index = RenderTasks.IndexOf(SelectedTask);
        if (index < RenderTasks.Count - 1)
        {
            RenderTasks.Move(index, RenderTasks.Count - 1);
        }
    }

    public void SetBlenderService(BlenderExeService blenderService)
    {
        _blenderService = blenderService;
    }

    private async Task StartNextAvailableTasks()
    {
        if (!IsQueueRunning) return;

        // 单任务模式：先停止所有正在运行的任务，然后启动下一个
        var runningTasks = RenderTasks.Where(t => t.Status == RenderTaskStatus.Running).ToList();
        foreach (var task in runningTasks)
        {
            task.StopRender();
        }

        // 等待一下确保任务停止
        await Task.Delay(100);

        // 启动下一个待处理的任务
        var pendingTask = RenderTasks.FirstOrDefault(t => t.Status == RenderTaskStatus.Pending);
        if (pendingTask == null) return;

        var taskCopy = pendingTask; // 避免闭包问题
        var runningTask = Task.Run(async () =>
        {
            try
            {
                // 为每个任务创建独立的BlenderExeService实例
                using var blenderService = new BlenderExeService(_blenderService!.BlenderPath);
                await taskCopy.StartRenderAsync(blenderService);
            }
            catch (Exception ex)
            {
                // 错误处理已在RenderTaskViewModel中完成
            }
            finally
            {
                // 任务完成后，尝试启动下一个任务
                if (AutoStartNext && IsQueueRunning)
                {
                    await StartNextAvailableTasks();
                }
            }
        });

        lock (_queueLock)
        {
            _runningTasks.Add(runningTask);
        }
    }

    private void SubscribeToTaskEvents(RenderTaskViewModel task)
    {
        task.StatusChanged += OnTaskStatusChanged;
        task.ProgressChanged += OnTaskProgressChanged;
    }

    private void UnsubscribeFromTaskEvents(RenderTaskViewModel task)
    {
        task.StatusChanged -= OnTaskStatusChanged;
        task.ProgressChanged -= OnTaskProgressChanged;
    }

    private void OnTaskStatusChanged(object? sender, RenderTaskStatusChangedEventArgs e)
    {
        UpdateQueueStatistics();
        
        var task = sender as RenderTaskViewModel;
        if (task != null)
        {
            TaskCompleted?.Invoke(this, new TaskCompletedEventArgs(task, e.Status));
        }

        // 如果启用了自动开始下一个任务，且当前任务完成，尝试启动下一个
        if (AutoStartNext && IsQueueRunning && 
            (e.Status == RenderTaskStatus.Completed || e.Status == RenderTaskStatus.Failed || e.Status == RenderTaskStatus.Cancelled))
        {
            _ = Task.Run(async () => await StartNextAvailableTasks());
        }
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        UpdateQueueStatistics();
    }

    private void UpdateQueueStatistics()
    {
        ActiveTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Running);
        CompletedTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Completed);
        FailedTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Failed || t.Status == RenderTaskStatus.Cancelled);

        // 计算整体进度
        if (RenderTasks.Any())
        {
            var totalProgress = RenderTasks.Sum(t => t.OverallProgress01);
            OverallQueueProgress = totalProgress / RenderTasks.Count;
        }
        else
        {
            OverallQueueProgress = 0.0;
        }

        // 更新队列状态文本
        if (IsQueueRunning)
        {
            if (ActiveTaskCount > 0)
            {
                QueueStatusText = $"运行中 ({ActiveTaskCount} 个任务)";
            }
            else if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending))
            {
                QueueStatusText = "等待中";
            }
            else
            {
                QueueStatusText = "队列完成";
                IsQueueRunning = false;
            }
        }
        else
        {
            if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending))
            {
                QueueStatusText = "队列空闲";
            }
            else if (RenderTasks.Any())
            {
                QueueStatusText = "队列完成";
            }
            else
            {
                QueueStatusText = "队列为空";
            }
        }
    }

    private async Task<string> SelectBlendFile()
    {
        // 这里需要从主视图模型获取文件选择功能
        // 暂时返回空字符串，实际实现需要依赖注入或事件
        return string.Empty;
    }

    private async Task<IEnumerable<string>> SelectMultipleBlendFiles()
    {
        // 这里需要从主视图模型获取文件选择功能
        // 暂时返回空集合，实际实现需要依赖注入或事件
        return Enumerable.Empty<string>();
    }

    public void Dispose()
    {
        StopQueue();
        
        foreach (var task in RenderTasks)
        {
            UnsubscribeFromTaskEvents(task);
            task.Dispose();
        }
        
        RenderTasks.Clear();
    }
}

// 队列状态变化事件参数
public class QueueStatusChangedEventArgs : EventArgs
{
    public string StatusMessage { get; }

    public QueueStatusChangedEventArgs(string statusMessage)
    {
        StatusMessage = statusMessage;
    }
}

// 任务完成事件参数
public class TaskCompletedEventArgs : EventArgs
{
    public RenderTaskViewModel Task { get; }
    public RenderTaskStatus Status { get; }

    public TaskCompletedEventArgs(RenderTaskViewModel task, RenderTaskStatus status)
    {
        Task = task;
        Status = status;
    }
}
