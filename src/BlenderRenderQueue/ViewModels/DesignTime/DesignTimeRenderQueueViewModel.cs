using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.Services.Business.Submission;

namespace BlenderRenderQueue.ViewModels.DesignTime;

/// <summary>
/// 设计时用的 RenderQueueViewModel
/// </summary>
public class DesignTimeRenderQueueViewModel : RenderQueueViewModel
{
    public DesignTimeRenderQueueViewModel() : base(new DesignTimeQueueApplicationService())
    {
        var service = (DesignTimeQueueApplicationService)QueueService;

        // 创建多个设计时任务
        var task1 = new DesignTimeRenderTaskViewModel();
        task1.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation1.blend";
        task1.Status = RenderTaskStatus.Running;
        task1.Progress01 = 0.75;
        task1.OverallProgress01 = 0.60;
        task1.CurrentFrame = 180;
        task1.CompletedFrames = 150;
        task1.OverrideScene = true;
        task1.SelectedSceneName = "Animation";
        
        var task2 = new DesignTimeRenderTaskViewModel();
        task2.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation2.blend";
        task2.Status = RenderTaskStatus.Pending;
        task2.Progress01 = 0.0;
        task2.OverallProgress01 = 0.0;
        task2.CurrentFrame = 0;
        task2.CompletedFrames = 0;
        task2.OverrideScene = false;
        task2.SelectedSceneName = "Scene";
        
        var task3 = new DesignTimeRenderTaskViewModel();
        task3.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation3.blend";
        task3.Status = RenderTaskStatus.Completed;
        task3.Progress01 = 1.0;
        task3.OverallProgress01 = 1.0;
        task3.CurrentFrame = 250;
        task3.CompletedFrames = 250;
        task3.OverrideScene = true;
        task3.SelectedSceneName = "Render_Scene";
        
        var task4 = new DesignTimeRenderTaskViewModel();
        task4.BlendFilePath = @"C:\Users\Design\Documents\Blender\LoadingFile.blend";
        task4.Status = RenderTaskStatus.Pending;
        task4.IsValid = true;
        task4.OverrideScene = false;
        // 设置加载状态
        task4.ScenePropertiesView.IsLoading = true;
        
        var task5 = new DesignTimeRenderTaskViewModel();
        task5.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation5.blend";
        task5.Status = RenderTaskStatus.Failed;
        task5.Progress01 = 0.0;
        task5.OverallProgress01 = 0.0;
        task5.OverrideScene = true;
        task5.SelectedSceneName = "Animation";

        service.SetDesignState(
            [
                task1, task2, task3, task4, task5
            ],
            task1,
            QueueExecutionState.Running,
            1,
            1,
            1,
            "运行中 (1 个任务)",
            "00:05:30");

        SelectedTask = task1;
    }
}

internal sealed class DesignTimeQueueApplicationService : IRenderQueueApplicationService
{
    public ObservableCollection<RenderTaskViewModel> RenderTasks { get; } = [];
    public RenderTaskViewModel? CurrentRenderingTask { get; private set; }
    public RenderQueueSnapshot Snapshot { get; private set; } = new();
    public bool AutoStartNext { get; set; } = true;
    public PostRenderBehavior PostRenderBehavior { get; set; } = PostRenderBehavior.None;
    public event EventHandler<RenderQueueSnapshot>? SnapshotChanged;
    public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged { add { } remove { } }
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted { add { } remove { } }
    public event EventHandler<string>? StatusMessageChanged { add { } remove { } }
    public event EventHandler<ConfirmDialogRequestedEventArgs>? ConfirmDialogRequested { add { } remove { } }

    public void SetDesignState(
        IReadOnlyList<RenderTaskViewModel> tasks,
        RenderTaskViewModel? currentTask,
        QueueExecutionState state,
        int activeTaskCount,
        int completedTaskCount,
        int failedTaskCount,
        string queueStatusText,
        string remainingTimeText)
    {
        RenderTasks.Clear();
        foreach (var task in tasks)
        {
            RenderTasks.Add(task);
        }

        CurrentRenderingTask = currentTask;
        Snapshot = new RenderQueueSnapshot
        {
            State = state,
            CurrentTaskId = currentTask?.Id,
            ActiveTaskCount = activeTaskCount,
            CompletedTaskCount = completedTaskCount,
            FailedTaskCount = failedTaskCount,
            QueueStatusText = queueStatusText,
            RemainingTimeText = remainingTimeText,
            AutoStartNext = AutoStartNext,
            PostRenderBehavior = PostRenderBehavior,
            CanStartQueue = true,
            CanStopQueue = true,
            CanPauseQueue = true,
            CanResumeQueue = false,
            CanClearTasks = true
        };

        SnapshotChanged?.Invoke(this, Snapshot);
    }

    public void SetGlobalRenderTimeout(int timeoutSeconds) { }
    public void SetGlobalMaxRetryAttempts(int maxRetryAttempts) { }
    public void SetVideoCodec(string codec) { }
    public void SetVideoQuality(string quality) { }
    public void SetBlenderPath(string blenderPath) { }
    public bool IsBlenderServiceReady() => true;
    public void AddBlendFiles(IEnumerable<string> filePaths) { }
    public void AddDroppedFiles(IEnumerable<string> filePaths) { }
    public void RemoveSelectedTask(RenderTaskViewModel? selectedTask, Action<RenderTaskViewModel?> setSelectedTask) { }
    public void RemoveTask(RenderTaskViewModel? taskToRemove, RenderTaskViewModel? selectedTask, Action<RenderTaskViewModel?> setSelectedTask) { }
    public void RemoveAllTasks() { }
    public void RemoveCompletedTasks() { }
    public Task StartQueueAsync() => Task.CompletedTask;
    public void StopQueue() { }
    public void PauseQueue() { }
    public Task ResumeQueueAsync() => Task.CompletedTask;
    public void MoveTaskUp(RenderTaskViewModel? selectedTask) { }
    public void MoveTaskDown(RenderTaskViewModel? selectedTask) { }
    public void MoveTaskToTop(RenderTaskViewModel? selectedTask) { }
    public void MoveTaskToBottom(RenderTaskViewModel? selectedTask) { }
    public void CopyTask(RenderTaskViewModel? taskToCopy, Action<RenderTaskViewModel?> setSelectedTask) { }
    public void RequestRemoveAllTasksConfirmation() { }
    public Task<LocalSubmissionResponse> SubmitTaskAsync(LocalSubmissionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LocalSubmissionResponse());
    public Task<LocalSubmissionResponse> StartQueueFromSubmissionAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LocalSubmissionResponse());
    public Task LoadQueueDataAsync() => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class DesignTimeWorkerHost : IBlenderWorkerHost
{
    public BlenderWorkerHostState State { get; } = new();
    public event System.Action<string>? OnOutputReceived { add { } remove { } }
    public event System.Action<string>? OnErrorReceived { add { } remove { } }
    public event System.Action<int>? OnProcessExited { add { } remove { } }

    public Task EnsureReadyAsync(string blenderExecutablePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<BlenderWorkerResponse> PingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BlenderWorkerResponse { Ok = true, WorkerState = "ready" });

    public Task<BlenderWorkerResponse> QueryFileInfoAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BlenderWorkerResponse { Ok = true, WorkerState = "ready" });

    public Task<BlenderWorkerResponse> LoadFileAsync(string blendFilePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BlenderWorkerResponse { Ok = true, WorkerState = "ready", CurrentFile = blendFilePath });

    public Task<BlenderWorkerResponse> RenderTaskAsync(BlenderWorkerRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BlenderWorkerResponse { Ok = true, WorkerState = "ready", CurrentFile = request.BlendFilePath, OutputVerified = true });

    public Task CancelCurrentRenderAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<BlenderWorkerRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new BlenderWorkerRecoveryResult { Recovered = true, Message = "Design-time host" });

    public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
