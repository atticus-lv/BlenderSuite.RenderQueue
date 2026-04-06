using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlenderRenderQueue.ViewModels;

public enum PostRenderBehavior
{
    None,
    Shutdown,
    Restart
}

public partial class RenderQueueViewModel : ViewModelBase
{
    private readonly IRenderQueueApplicationService _queueService;
    protected internal IRenderQueueApplicationService QueueService => _queueService;

    [ObservableProperty]
    private RenderTaskViewModel? _selectedTask;

    public RenderQueueViewModel(IRenderQueueApplicationService queueService)
    {
        _queueService = queueService;
        RenderTasks = queueService.RenderTasks;

        _queueService.SnapshotChanged += OnSnapshotChanged;
        _queueService.QueueStatusChanged += (s, e) => QueueStatusChanged?.Invoke(this, e);
        _queueService.TaskCompleted += (s, e) => TaskCompleted?.Invoke(this, e);
        _queueService.StatusMessageChanged += (s, e) => StatusMessageChanged?.Invoke(this, e);
        _queueService.ConfirmDialogRequested += (s, e) => ConfirmDialogRequested?.Invoke(this, e);

        RenderTasks.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<RenderTaskViewModel>())
                {
                    item.OpenInBlenderRequested -= OnTaskOpenInBlenderRequested;
                    item.OpenFileDirectoryRequested -= OnTaskOpenFileDirectoryRequested;
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<RenderTaskViewModel>())
                {
                    item.OpenInBlenderRequested += OnTaskOpenInBlenderRequested;
                    item.OpenFileDirectoryRequested += OnTaskOpenFileDirectoryRequested;
                }
            }

            NotifyStateChanged();
        };

        foreach (var task in RenderTasks)
        {
            task.OpenInBlenderRequested += OnTaskOpenInBlenderRequested;
            task.OpenFileDirectoryRequested += OnTaskOpenFileDirectoryRequested;
        }

        NotifyStateChanged();
    }

    public ObservableCollection<RenderTaskViewModel> RenderTasks { get; }
    public RenderTaskViewModel? CurrentRenderingTask => _queueService.CurrentRenderingTask;
    public QueueState QueueState => Snapshot.State switch
    {
        QueueExecutionState.Running => QueueState.Running,
        QueueExecutionState.Paused => QueueState.Paused,
        QueueExecutionState.Completed => QueueState.Completed,
        QueueExecutionState.Error => QueueState.Error,
        _ => QueueState.Idle
    };

    private RenderQueueSnapshot Snapshot => _queueService.Snapshot;

    public int ActiveTaskCount => Snapshot.ActiveTaskCount;
    public int CompletedTaskCount => Snapshot.CompletedTaskCount;
    public int FailedTaskCount => Snapshot.FailedTaskCount;
    public string QueueStatusText => Snapshot.QueueStatusText;
    public bool AutoStartNext
    {
        get => _queueService.AutoStartNext;
        set
        {
            if (_queueService.AutoStartNext == value)
            {
                return;
            }

            _queueService.AutoStartNext = value;
            OnPropertyChanged();
        }
    }

    public PostRenderBehavior PostRenderBehavior
    {
        get => _queueService.PostRenderBehavior;
        set
        {
            if (_queueService.PostRenderBehavior == value)
            {
                return;
            }

            _queueService.PostRenderBehavior = value;
            NotifyStateChanged();
        }
    }

    public string PostRenderBehaviorText
    {
        get
        {
            var prefix = Localizer.Localizer.Instance["SystemControl_PostRenderBehavior"];
            var action = PostRenderBehavior switch
            {
                PostRenderBehavior.None => Localizer.Localizer.Instance["SystemControl_None"],
                PostRenderBehavior.Shutdown => Localizer.Localizer.Instance["SystemControl_Shutdown"],
                PostRenderBehavior.Restart => Localizer.Localizer.Instance["SystemControl_Restart"],
                _ => Localizer.Localizer.Instance["SystemControl_None"]
            };
            return $"{prefix}: {action}";
        }
    }

    public IBrush PostRenderBehaviorIconColor => PostRenderBehavior switch
    {
        PostRenderBehavior.None => GetResourceBrush("SukiTextColor") ?? Brushes.Gray,
        PostRenderBehavior.Shutdown => GetResourceBrush("SukiDangerColor") ?? Brushes.Red,
        PostRenderBehavior.Restart => GetResourceBrush("SukiWarningColor") ?? Brushes.Orange,
        _ => GetResourceBrush("SukiTextColor") ?? Brushes.Gray
    };

    public string PostRenderBehaviorIcon => PostRenderBehavior switch
    {
        PostRenderBehavior.None => "ArrowRight",
        PostRenderBehavior.Shutdown => "Power",
        PostRenderBehavior.Restart => "Restart",
        _ => "ArrowRight"
    };

    public bool IsQueueRunning => QueueState == QueueState.Running;
    public bool IsQueueActive => QueueState is QueueState.Running or QueueState.Paused;
    public bool HasNoTasks => RenderTasks.Count == 0;
    public bool HasRunningTasks => ActiveTaskCount > 0;
    public int TotalFrames => Snapshot.TotalFrames;
    public double CompletedFrameProgress => Snapshot.CompletedFrameProgress;
    public int CompletedFrames => (int)Snapshot.CompletedFrameProgress;
    public double OverallQueueProgress => Snapshot.OverallProgress01;
    public int OverallQueueProgressInt => (int)Math.Round(Snapshot.OverallProgress01 * 100, MidpointRounding.AwayFromZero);
    public string RemainingTimeText => Snapshot.RemainingTimeText;
    public bool CanStartQueue => Snapshot.CanStartQueue;
    public bool CanShowStartQueue => !HasNoTasks && QueueState is QueueState.Idle or QueueState.Completed;
    public bool CanStopQueue => Snapshot.CanStopQueue;
    public bool CanPauseQueue => Snapshot.CanPauseQueue;
    public bool CanResumeQueue => Snapshot.CanResumeQueue;
    public bool CanClearTasks => Snapshot.CanClearTasks;

    public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<ConfirmDialogRequestedEventArgs>? ConfirmDialogRequested;

    public void SetGlobalRenderTimeout(int timeoutSeconds) => _queueService.SetGlobalRenderTimeout(timeoutSeconds);
    public void SetGlobalMaxRetryAttempts(int maxRetryAttempts) => _queueService.SetGlobalMaxRetryAttempts(maxRetryAttempts);
    public void SetVideoCodec(string codec) => _queueService.SetVideoCodec(codec);
    public void SetVideoQuality(string quality) => _queueService.SetVideoQuality(quality);
    public void SetBlenderPath(string blenderPath)
    {
        SettingsPathCache.LastKnownBlenderPath = blenderPath;
        _queueService.SetBlenderPath(blenderPath);
    }
    public bool IsBlenderServiceReady() => _queueService.IsBlenderServiceReady();
    public Task LoadQueueDataAsync() => _queueService.LoadQueueDataAsync();

    [RelayCommand]
    private Task AddTask() => _queueService.AddTaskAsync();

    [RelayCommand]
    private Task AddMultipleTasks() => _queueService.AddMultipleTasksAsync();

    [RelayCommand]
    private void AddDroppedFiles(IEnumerable<IStorageItem> files) => _queueService.AddDroppedFiles(files);

    [RelayCommand]
    private void RemoveSelectedTask() => _queueService.RemoveSelectedTask(SelectedTask, task => SelectedTask = task);

    [RelayCommand]
    private void RemoveTask(RenderTaskViewModel? taskToRemove) => _queueService.RemoveTask(taskToRemove, SelectedTask, task => SelectedTask = task);

    [RelayCommand]
    private void RemoveAllTasks() => _queueService.RequestRemoveAllTasksConfirmation();

    [RelayCommand]
    private void RemoveCompletedTasks() => _queueService.RemoveCompletedTasks();

    [RelayCommand]
    private Task StartQueue() => _queueService.StartQueueAsync();

    [RelayCommand]
    private void StopQueue() => _queueService.StopQueue();

    [RelayCommand]
    private void PauseQueue() => _queueService.PauseQueue();

    [RelayCommand]
    private Task ResumeQueue() => _queueService.ResumeQueueAsync();

    [RelayCommand]
    private void MoveTaskUp() => _queueService.MoveTaskUp(SelectedTask);

    [RelayCommand]
    private void MoveTaskDown() => _queueService.MoveTaskDown(SelectedTask);

    [RelayCommand]
    private void MoveTaskToTop() => _queueService.MoveTaskToTop(SelectedTask);

    [RelayCommand]
    private void MoveTaskToBottom() => _queueService.MoveTaskToBottom(SelectedTask);

    [RelayCommand]
    private void SetPostRenderBehavior(string behavior)
    {
        if (!Enum.TryParse<PostRenderBehavior>(behavior, out var parsedBehavior))
        {
            return;
        }

        PostRenderBehavior = parsedBehavior;
    }

    [RelayCommand]
    private void CopyTask(RenderTaskViewModel? taskToCopy) => _queueService.CopyTask(taskToCopy, task => SelectedTask = task);

    public Task<LocalSubmissionResponse> SubmitTaskAsync(LocalSubmissionRequest request,
        System.Threading.CancellationToken cancellationToken = default)
        => _queueService.SubmitTaskAsync(request, cancellationToken);

    public Task<LocalSubmissionResponse> StartQueueFromSubmissionAsync(System.Threading.CancellationToken cancellationToken = default)
        => _queueService.StartQueueFromSubmissionAsync(cancellationToken);

    public bool SelectTask(Guid taskId)
    {
        var task = RenderTasks.FirstOrDefault(item => item.Id == taskId);
        if (task == null)
        {
            return false;
        }

        SelectedTask = task;
        return true;
    }

    private void OnSnapshotChanged(object? sender, RenderQueueSnapshot e)
    {
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CurrentRenderingTask));
        OnPropertyChanged(nameof(QueueState));
        OnPropertyChanged(nameof(ActiveTaskCount));
        OnPropertyChanged(nameof(CompletedTaskCount));
        OnPropertyChanged(nameof(FailedTaskCount));
        OnPropertyChanged(nameof(QueueStatusText));
        OnPropertyChanged(nameof(AutoStartNext));
        OnPropertyChanged(nameof(PostRenderBehavior));
        OnPropertyChanged(nameof(PostRenderBehaviorText));
        OnPropertyChanged(nameof(PostRenderBehaviorIconColor));
        OnPropertyChanged(nameof(PostRenderBehaviorIcon));
        OnPropertyChanged(nameof(IsQueueRunning));
        OnPropertyChanged(nameof(IsQueueActive));
        OnPropertyChanged(nameof(HasNoTasks));
        OnPropertyChanged(nameof(HasRunningTasks));
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(CompletedFrameProgress));
        OnPropertyChanged(nameof(CompletedFrames));
        OnPropertyChanged(nameof(OverallQueueProgress));
        OnPropertyChanged(nameof(OverallQueueProgressInt));
        OnPropertyChanged(nameof(RemainingTimeText));
        OnPropertyChanged(nameof(CanStartQueue));
        OnPropertyChanged(nameof(CanShowStartQueue));
        OnPropertyChanged(nameof(CanStopQueue));
        OnPropertyChanged(nameof(CanPauseQueue));
        OnPropertyChanged(nameof(CanResumeQueue));
        OnPropertyChanged(nameof(CanClearTasks));
    }

    private static IBrush? GetResourceBrush(string resourceKey)
    {
        if (Avalonia.Application.Current?.TryGetResource(resourceKey, Avalonia.Styling.ThemeVariant.Default, out var resource) == true)
        {
            return resource as IBrush;
        }

        return null;
    }

    private void OnTaskOpenInBlenderRequested(object? sender, OpenInBlenderRequestedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(e.FilePath) || !File.Exists(e.FilePath))
            {
                return;
            }

            if (!TryGetConfiguredBlenderPath(out var configuredBlenderPath))
            {
                return;
            }

            var blenderExecutable = GetBestBlenderExecutable(configuredBlenderPath);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = blenderExecutable,
                Arguments = $"\"{e.FilePath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = false
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] Error opening file in Blender: {ex.Message}");
        }
    }

    private void OnTaskOpenFileDirectoryRequested(object? sender, OpenSysDirectoryRequestedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(e.FilePath))
            {
                FileSystemHelper.OpenFileDirectory(e.FilePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] Error opening file directory: {ex.Message}");
        }
    }

    private bool TryGetConfiguredBlenderPath(out string blenderPath)
    {
        blenderPath = SettingsPathCache.LastKnownBlenderPath;
        return !string.IsNullOrWhiteSpace(blenderPath) && File.Exists(blenderPath);
    }

    private static string GetBestBlenderExecutable(string blenderPath)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return blenderPath;
            }

            var directory = Path.GetDirectoryName(blenderPath);
            if (string.IsNullOrEmpty(directory))
            {
                return blenderPath;
            }

            var launcherPath = Path.Combine(directory, "blender-launcher.exe");
            if (File.Exists(launcherPath))
            {
                return launcherPath;
            }

            if (string.Equals(Path.GetFileName(blenderPath), "blender.exe", StringComparison.OrdinalIgnoreCase))
            {
                var parentDirectory = Directory.GetParent(directory)?.FullName;
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    var parentLauncherPath = Path.Combine(parentDirectory, "blender-launcher.exe");
                    if (File.Exists(parentLauncherPath))
                    {
                        return parentLauncherPath;
                    }
                }
            }

            return blenderPath;
        }
        catch
        {
            return blenderPath;
        }
    }

    public void Dispose()
    {
        _queueService.SnapshotChanged -= OnSnapshotChanged;
        foreach (var task in RenderTasks)
        {
            task.OpenInBlenderRequested -= OnTaskOpenInBlenderRequested;
            task.OpenFileDirectoryRequested -= OnTaskOpenFileDirectoryRequested;
        }
    }
}

internal static class SettingsPathCache
{
    public static string LastKnownBlenderPath { get; set; } = string.Empty;
}

public class QueueStatusChangedEventArgs(string statusMessage) : EventArgs
{
    public string StatusMessage { get; } = statusMessage;
}

public class TaskCompletedEventArgs(RenderTaskViewModel task, RenderTaskStatus status) : EventArgs
{
    public RenderTaskViewModel Task { get; } = task;
    public RenderTaskStatus Status { get; } = status;
}

public class ConfirmDialogRequestedEventArgs(
    string title,
    string content,
    string cancelButtonText,
    string confirmButtonText,
    Action confirmAction)
    : EventArgs
{
    public string Title { get; } = title;
    public string Content { get; } = content;
    public string CancelButtonText { get; } = cancelButtonText;
    public string ConfirmButtonText { get; } = confirmButtonText;
    public Action ConfirmAction { get; } = confirmAction;
}
