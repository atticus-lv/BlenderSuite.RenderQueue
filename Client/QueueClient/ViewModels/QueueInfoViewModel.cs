using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlenderSuite.RenderQueue.ViewModels;

public partial class QueueInfoViewModel : ViewModelBase
{
    private readonly ApiService _apiService;
    private readonly ConnectionViewModel _connectionViewModel;

    [ObservableProperty]
    private QueueStatusResponse? _queueStatus;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _lastUpdateTime = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public QueueInfoViewModel(ConnectionViewModel connectionViewModel)
    {
        _connectionViewModel = connectionViewModel;
        _apiService = connectionViewModel.GetApiService();
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
                LastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
            }
            else
            {
                ErrorMessage = "Failed to fetch queue status";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // 计算属性
    public string QueueStateText => QueueStatus?.QueueState ?? "Unknown";
    
    public double OverallProgress => QueueStatus?.OverallProgress ?? 0.0;
    
    public string ProgressText => $"{OverallProgress:P1}";
    
    public int TotalTasks => (QueueStatus?.ActiveTaskCount ?? 0) + 
                            (QueueStatus?.CompletedTaskCount ?? 0) + 
                            (QueueStatus?.FailedTaskCount ?? 0);
    
    public string TaskSummary => $"Active: {QueueStatus?.ActiveTaskCount ?? 0}, " +
                                $"Completed: {QueueStatus?.CompletedTaskCount ?? 0}, " +
                                $"Failed: {QueueStatus?.FailedTaskCount ?? 0}";
    
    public string FrameSummary => $"{QueueStatus?.CompletedFrames ?? 0} / {QueueStatus?.TotalFrames ?? 0} frames";
    
    public string RemainingTime => QueueStatus?.RemainingTime ?? "Unknown";
    
    public string CurrentTaskName => QueueStatus?.CurrentTask?.FileName ?? "No active task";
    
    public double CurrentTaskProgress => QueueStatus?.CurrentTask?.Progress ?? 0.0;
    
    public string CurrentTaskProgressText => $"{CurrentTaskProgress:P1}";
}
