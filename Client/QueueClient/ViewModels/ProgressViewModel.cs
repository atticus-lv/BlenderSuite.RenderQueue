using System;
using System.Collections.ObjectModel;
using System.Linq;
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

    public ProgressViewModel(ConnectionViewModel connectionViewModel)
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
            var tasks = await _apiService.GetTasksAsync();
            if (tasks != null)
            {
                Tasks.Clear();
                foreach (var task in tasks)
                {
                    Tasks.Add(task);
                }
                LastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
            }
            else
            {
                ErrorMessage = "Failed to fetch tasks";
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
    public int TotalTasks => Tasks.Count;
    
    public int ActiveTasks => Tasks.Count(t => t.Status == "Running" || t.Status == "Pending");
    
    public int CompletedTasks => Tasks.Count(t => t.Status == "Completed");
    
    public int FailedTasks => Tasks.Count(t => t.Status == "Failed");
    
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
}
