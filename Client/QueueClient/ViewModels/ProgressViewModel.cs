using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlenderSuite.RenderQueue.ViewModels;

public partial class ProgressViewModel : ViewModelBase
{
    private readonly ApiService _apiService;
    private readonly ConnectionViewModel _connectionViewModel;
    private Timer? _refreshTimer;
    private readonly object _refreshLock = new object();

    [ObservableProperty]
    private ObservableCollection<TaskInfoResponse> _tasks = new();

    [ObservableProperty]
    private TaskInfoResponse? _selectedTask;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _lastUpdateTime = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _autoRefreshEnabled = true;

    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        if (_connectionViewModel.IsConnected)
        {
            if (value)
            {
                StartAutoRefresh();
            }
            else
            {
                StopAutoRefresh();
            }
        }
    }

    public ProgressViewModel(ConnectionViewModel connectionViewModel)
    {
        _connectionViewModel = connectionViewModel;
        _apiService = connectionViewModel.GetApiService();
        
        // 监听连接状态变化
        _connectionViewModel.PropertyChanged += OnConnectionPropertyChanged;
    }

    private async void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.IsConnected))
        {
            if (_connectionViewModel.IsConnected)
            {
                // 连接成功后自动刷新
                await RefreshAsync();
                // 启动自动刷新定时器
                StartAutoRefresh();
            }
            else
            {
                // 断开连接时清空数据和停止定时器
                StopAutoRefresh();
                Tasks.Clear();
                ErrorMessage = "Disconnected from server";
                LastUpdateTime = string.Empty;
            }
        }
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh(); // 确保之前的定时器已停止
        
        if (AutoRefreshEnabled)
        {
            _refreshTimer = new Timer(async _ =>
            {
                if (_connectionViewModel.IsConnected && !IsLoading)
                {
                    try
                    {
                        await RefreshAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ProgressViewModel] Auto refresh error: {ex.Message}");
                    }
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
            var tasks = await _apiService.GetTasksAsync();
            if (tasks != null)
            {
                Tasks.Clear();
                foreach (var task in tasks)
                {
                    Tasks.Add(task);
                }
                LastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
                ErrorMessage = string.Empty; // 清除之前的错误
                
                // 手动触发计算属性更新通知
                OnPropertyChanged(nameof(TotalTasks));
                OnPropertyChanged(nameof(ActiveTasks));
                OnPropertyChanged(nameof(CompletedTasks));
                OnPropertyChanged(nameof(FailedTasks));
                OnPropertyChanged(nameof(OverallProgress));
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(TaskSummary));
            }
            else
            {
                ErrorMessage = "Failed to fetch tasks - check server connection and API endpoint";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            Console.WriteLine($"[ProgressViewModel] RefreshAsync exception: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // 计算属性
    public int TotalTasks => Tasks.Count;
    
    public int ActiveTasks => Tasks.Count(t => t.Status == RenderTaskStatus.Running || t.Status == RenderTaskStatus.Pending);
    
    public int CompletedTasks => Tasks.Count(t => t.Status == RenderTaskStatus.Completed);
    
    public int FailedTasks => Tasks.Count(t => t.Status == RenderTaskStatus.Failed);
    
    public double OverallProgress
    {
        get
        {
            if (Tasks.Count == 0) return 0.0;
            return Tasks.Average(t => t.OverallProgress);
        }
    }
    
    public string ProgressText => $"{OverallProgress:P1}";
    
    public string TaskSummary => $"Total: {TotalTasks}, Active: {ActiveTasks}, Completed: {CompletedTasks}, Failed: {FailedTasks}";

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
