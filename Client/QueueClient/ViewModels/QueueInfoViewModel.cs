using System;
using System.ComponentModel;
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
    private QueueStatusResponse? _queueStatus;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

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

    public string CurrentTaskName => QueueStatus?.CurrentTask?.FileName ?? "No active task";

    public double CurrentTaskProgress => QueueStatus?.CurrentTask?.Progress ?? 0.0;

    public string CurrentTaskProgressText => $"{CurrentTaskProgress:P1}";

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