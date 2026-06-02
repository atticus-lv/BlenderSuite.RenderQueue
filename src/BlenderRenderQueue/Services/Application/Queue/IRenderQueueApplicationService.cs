using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    void AddBlendFiles(IEnumerable<string> filePaths);
    void AddDroppedFiles(IEnumerable<string> filePaths);
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

    Task LoadQueueDataAsync();
}
