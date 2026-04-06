using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Services.Application.Queue;

public interface IRenderQueueApplicationService : IDisposable
{
    ObservableCollection<RenderTaskViewModel> RenderTasks { get; }
    RenderTaskViewModel? CurrentRenderingTask { get; }
    RenderQueueSnapshot Snapshot { get; }
    bool AutoStartNext { get; set; }
    PostRenderBehavior PostRenderBehavior { get; set; }
    event EventHandler<RenderQueueSnapshot>? SnapshotChanged;
    event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
    event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    event EventHandler<string>? StatusMessageChanged;
    event EventHandler<ConfirmDialogRequestedEventArgs>? ConfirmDialogRequested;

    void SetGlobalRenderTimeout(int timeoutSeconds);
    void SetGlobalMaxRetryAttempts(int maxRetryAttempts);
    void SetVideoCodec(string codec);
    void SetVideoQuality(string quality);
    void SetBlenderPath(string blenderPath);
    bool IsBlenderServiceReady();

    Task AddTaskAsync();
    Task AddMultipleTasksAsync();
    void AddDroppedFiles(IEnumerable<IStorageItem> files);
    void RemoveSelectedTask(RenderTaskViewModel? selectedTask, Action<RenderTaskViewModel?> setSelectedTask);
    void RemoveTask(RenderTaskViewModel? taskToRemove, RenderTaskViewModel? selectedTask, Action<RenderTaskViewModel?> setSelectedTask);
    void RemoveAllTasks();
    void RemoveCompletedTasks();
    Task StartQueueAsync();
    void StopQueue();
    void PauseQueue();
    Task ResumeQueueAsync();
    void MoveTaskUp(RenderTaskViewModel? selectedTask);
    void MoveTaskDown(RenderTaskViewModel? selectedTask);
    void MoveTaskToTop(RenderTaskViewModel? selectedTask);
    void MoveTaskToBottom(RenderTaskViewModel? selectedTask);
    void CopyTask(RenderTaskViewModel? taskToCopy, Action<RenderTaskViewModel?> setSelectedTask);
    void RequestRemoveAllTasksConfirmation();

    Task<LocalSubmissionResponse> SubmitTaskAsync(LocalSubmissionRequest request, CancellationToken cancellationToken = default);
    Task<LocalSubmissionResponse> StartQueueFromSubmissionAsync(CancellationToken cancellationToken = default);
    Task LoadQueueDataAsync();
}
