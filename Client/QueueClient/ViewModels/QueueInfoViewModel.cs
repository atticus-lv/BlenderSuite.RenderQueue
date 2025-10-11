using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly object _refreshLock = new object();

    [ObservableProperty]
    private OptimizedQueueStatusResponse? _queueStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OptimizedTaskInfo> _allTasks = new();

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
            _refreshTimer = new Timer(async void (_) =>
            {
                if (!_connectionViewModel.IsConnected || IsLoading) return;
                try
                {
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[QueueInfoViewModel] Auto refresh error: {ex.Message}");
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
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
            ErrorMessage = "Not connected to server";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var status = await _apiService.GetQueueStatusAsync();
            if (status != null)
            {
                QueueStatus = status;
                // 高效更新任务列表
                UpdateTasksList(status.Tasks ?? new List<OptimizedTaskInfo>());
                ErrorMessage = string.Empty; // 清除之前的错误

                // 手动触发计算属性更新通知
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
            }
            else
            {
                ErrorMessage = "Failed to fetch queue status - check server connection and API endpoint";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            Console.WriteLine($"[QueueInfoViewModel] RefreshAsync exception: {ex}");
        }
        finally
        {
            IsLoading = false;
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
        // 去重处理 - 按TaskId去重
        var distinctTasks = newTasks
            .GroupBy(t => t.TaskId)
            .Select(g => g.First())
            .ToList();
        
        // 检查是否有任何变化
        bool hasChanges = false;
        
        if (AllTasks.Count != distinctTasks.Count)
        {
            hasChanges = true;
        }
        else
        {
            for (int i = 0; i < distinctTasks.Count; i++)
            {
                var newTask = distinctTasks[i];
                var existingTask = AllTasks[i];
                
                if (existingTask.TaskId != newTask.TaskId ||
                    existingTask.Status != newTask.Status ||
                    existingTask.Enable != newTask.Enable ||
                    existingTask.OverallProgress != newTask.OverallProgress ||
                    existingTask.CurrentFrame != newTask.CurrentFrame)
                {
                    hasChanges = true;
                    break;
                }
            }
        }
        
        // 只有在有变化时才更新
        if (hasChanges)
        {
            AllTasks.Clear();
            foreach (var task in distinctTasks)
            {
                AllTasks.Add(task);
            }
        }
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