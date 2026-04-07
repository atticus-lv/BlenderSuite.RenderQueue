using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QueueClient.ViewModels;

public partial class QueueInfoViewModel : ViewModelBase
{
    private readonly ApiService _apiService;
    private readonly ConnectionViewModel _connectionViewModel;
    private Timer? _refreshTimer;
    private int _refreshInProgress;

    [ObservableProperty]
    private OptimizedQueueStatusResponse? _queueStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OptimizedTaskInfo> _allTasks = [];

    public QueueInfoViewModel(ConnectionViewModel connectionViewModel)
    {
        _connectionViewModel = connectionViewModel;
        _apiService = connectionViewModel.GetApiService();

        // 监听连接状态变化
        _connectionViewModel.PropertyChanged += OnConnectionPropertyChanged;
    }

    private async void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConnectionViewModel.IsConnected) when _connectionViewModel.IsConnected:
                // 连接成功后自动刷新
                await RefreshAsync();
                // 启动自动刷新定时器
                StartAutoRefresh();
                break;
            case nameof(ConnectionViewModel.IsConnected):
                // 断开连接时清空数据和停止定时器
                StopAutoRefresh();
                QueueStatus = null;
                ErrorMessage = "Disconnected from server";
                break;
            // 自动刷新设置变化时重新配置定时器
            case nameof(ConnectionViewModel.AutoRefreshEnabled) when !_connectionViewModel.IsConnected:
                return;
            case nameof(ConnectionViewModel.AutoRefreshEnabled) when _connectionViewModel.AutoRefreshEnabled:
                StartAutoRefresh();
                break;
            case nameof(ConnectionViewModel.AutoRefreshEnabled):
                StopAutoRefresh();
                break;
        }
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh(); // 确保之前的定时器已停止

        if (_connectionViewModel.AutoRefreshEnabled)
        {
            _refreshTimer = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    private void StopAutoRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_connectionViewModel.IsConnected)
        {
            await Dispatcher.UIThread.InvokeAsync(() => ErrorMessage = "Not connected to server");
            return;
        }

        if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
                ErrorMessage = string.Empty;
            });

            var status = await _apiService.GetQueueStatusAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (status != null)
                {
                    QueueStatus = status;
                    UpdateTasksList(status.Tasks);
                    ErrorMessage = string.Empty;
                    NotifyDerivedPropertiesChanged();
                }
                else
                {
                    ErrorMessage = "Failed to fetch queue status - check server connection and API endpoint";
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => ErrorMessage = $"Error: {ex.Message}");
            Console.WriteLine($"[QueueInfoViewModel] RefreshAsync exception: {ex}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    public string QueueStateText => QueueStatus?.QueueState.ToString() ?? "Unknown";

    public double OverallProgress => QueueStatus?.OverallProgress ?? 0.0;

    public string ProgressText => $"{OverallProgress:P1}";

    public int TotalTasks => (QueueStatus?.ActiveTaskCount ?? 0) +
                             (QueueStatus?.CompletedTaskCount ?? 0) +
                             (QueueStatus?.FailedTaskCount ?? 0);

    public string TaskSummary => $"Completed: {QueueStatus?.CompletedTaskCount ?? 0}, " +
                                 $"Failed: {QueueStatus?.FailedTaskCount ?? 0}";

    public string FrameSummary => $"{QueueStatus?.CompletedFrames ?? 0} / {QueueStatus?.TotalFrames ?? 0} frames";

    public string RemainingTime => QueueStatus?.RemainingTime ?? "Unknown";

    public string CurrentTaskName => GetCurrentTask()?.FileName ?? "No active task";

    public double CurrentTaskProgress => GetCurrentTask()?.OverallProgress ?? 0.0;

    public string CurrentTaskProgressText => $"{CurrentTaskProgress:P1}";

    /// <summary>
    /// 是否有任务
    /// </summary>
    public bool HasTasks => AllTasks.Any();

    /// <summary>
    /// 队列是否正在运行
    /// </summary>
    public bool IsQueueRunning => QueueStatus?.QueueState == QueueState.Running;

    /// <summary>
    /// 检查任务是否为当前正在运行的任务
    /// </summary>
    public bool IsCurrentTask(OptimizedTaskInfo task)
    {
        return task.Status == RenderTaskStatus.Running;
    }

    /// <summary>
    /// 智能更新任务列表
    /// </summary>
    private void UpdateTasksList(List<OptimizedTaskInfo> newTasks)
    {
        // 去重处理 - 按TaskId去重，保留第一个
        var distinctTasks = newTasks
            .GroupBy(t => t.TaskId)
            .Select(g => g.First())
            .ToList();
        
            
        // 使用增量更新策略，避免清空整个集合
        var newTaskIds = distinctTasks.Select(t => t.TaskId).ToHashSet();
        var existingTaskIds = AllTasks.Select(t => t.TaskId).ToHashSet();
        
        var tasksToRemove = AllTasks.Where(t => !newTaskIds.Contains(t.TaskId)).ToList();
        foreach (var task in tasksToRemove)
        {
            Console.WriteLine($"[QueueInfoViewModel] Removing task: TaskId={task.TaskId}, FileName={task.FileName}");
            AllTasks.Remove(task);
        }
        
        // 添加新任务
        var tasksToAdd = distinctTasks.Where(t => !existingTaskIds.Contains(t.TaskId)).ToList();
        foreach (var task in tasksToAdd)
        {
            Console.WriteLine($"[QueueInfoViewModel] Adding new task: TaskId={task.TaskId}, FileName={task.FileName}");
            AllTasks.Add(task);
        }
        
        // 更新现有任务的数据 - 替换整个对象以触发UI更新
        for (var i = 0; i < AllTasks.Count; i++)
        {
            var existingTask = AllTasks[i];
            var newTask = distinctTasks.FirstOrDefault(t => t.TaskId == existingTask.TaskId);

            if (newTask == null) continue;
            var hasChanges = existingTask.Status != newTask.Status ||
                             existingTask.Enable != newTask.Enable ||
                             Math.Abs(existingTask.OverallProgress - newTask.OverallProgress) > 0.001 ||
                             existingTask.CurrentFrame != newTask.CurrentFrame ||
                             existingTask.FileName != newTask.FileName;

            if (!hasChanges) continue;
            Console.WriteLine($"[QueueInfoViewModel] Updating task: TaskId={newTask.TaskId}, FileName={newTask.FileName}");
            AllTasks[i] = newTask;
        }
        
        
        Console.WriteLine($"[QueueInfoViewModel] Final AllTasks count: {AllTasks.Count}");
        OnPropertyChanged(nameof(HasTasks));
    }

    private void NotifyDerivedPropertiesChanged()
    {
        OnPropertyChanged(nameof(QueueStateText));
        OnPropertyChanged(nameof(OverallProgress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(TotalTasks));
        OnPropertyChanged(nameof(TaskSummary));
        OnPropertyChanged(nameof(FrameSummary));
        OnPropertyChanged(nameof(RemainingTime));
        OnPropertyChanged(nameof(CurrentTaskName));
        OnPropertyChanged(nameof(CurrentTaskProgress));
        OnPropertyChanged(nameof(CurrentTaskProgressText));
        OnPropertyChanged(nameof(IsQueueRunning));
        OnPropertyChanged(nameof(HasTasks));
    }

    /// <summary>
    /// 获取当前正在渲染的任务
    /// </summary>
    private OptimizedTaskInfo? GetCurrentTask()
    {
        return QueueStatus?.Tasks.FirstOrDefault(task => task.Status == RenderTaskStatus.Running);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionViewModel.PropertyChanged -= OnConnectionPropertyChanged;
            StopAutoRefresh();
        }

        base.Dispose(disposing);
    }
}
