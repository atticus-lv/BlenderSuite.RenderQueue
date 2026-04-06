using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.Services.UI;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Services.Application.Queue;

public sealed class RenderQueueApplicationService : IRenderQueueApplicationService
{
    private readonly IBlenderWorkerHost _workerHost;
    private readonly IRenderTaskExecutionService _executionService;
    private readonly IDataPersistenceService _dataPersistenceService;
    private readonly List<Task> _runningTasks = [];
    private readonly object _queueLock = new();
    private readonly Queue<TimeSpan> _recentFrameRenderTimes = new();
    private const int MaxRecentFrames = 3;
    private readonly System.Timers.Timer _remainingTimeTimer;

    private RenderTaskViewModel? _pausedTask;
    private int _pausedFrame;
    private BlenderProcessService? _blenderProcessService;
    private BlenderVideoService? _blenderVideoService;
    private BlenderProcessService? _processService;
    private string? _blenderPath;
    private int _globalRenderTimeoutSeconds = 300;
    private int _globalMaxRetryAttempts = 3;
    private string _videoCodec = "H264";
    private string _videoQuality = "PERC_LOSSLESS";
    private QueueState _queueState = QueueState.Idle;
    private string _queueStatusText = "Queue_Idle";
    private string _remainingTimeText = string.Empty;
    private int _activeTaskCount;
    private int _completedTaskCount;
    private int _failedTaskCount;
    private bool _disposed;

    public RenderQueueApplicationService(
        IBlenderWorkerHost workerHost,
        IRenderTaskExecutionService executionService,
        IDataPersistenceService dataPersistenceService)
    {
        _workerHost = workerHost;
        _executionService = executionService;
        _dataPersistenceService = dataPersistenceService;

        RenderTasks = [];
        _remainingTimeTimer = new System.Timers.Timer(1000);
        _remainingTimeTimer.Elapsed += OnRemainingTimeTimerElapsed;
        _remainingTimeTimer.AutoReset = true;

        RenderTasks.CollectionChanged += (_, _) =>
        {
            PublishSnapshot();
            AutoSaveQueueData();
        };

        Snapshot = BuildSnapshot();
    }

    public ObservableCollection<RenderTaskViewModel> RenderTasks { get; }
    public RenderTaskViewModel? CurrentRenderingTask { get; private set; }
    public bool AutoStartNext { get; set; } = true;
    public PostRenderBehavior PostRenderBehavior { get; set; } = PostRenderBehavior.None;
    public RenderQueueSnapshot Snapshot { get; private set; }

    public event EventHandler<RenderQueueSnapshot>? SnapshotChanged;
    public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<ConfirmDialogRequestedEventArgs>? ConfirmDialogRequested;

    public void SetGlobalRenderTimeout(int timeoutSeconds)
    {
        _globalRenderTimeoutSeconds = timeoutSeconds;
        foreach (var task in RenderTasks)
        {
            task.SetGlobalRenderTimeout(timeoutSeconds);
        }
    }

    public void SetGlobalMaxRetryAttempts(int maxRetryAttempts)
    {
        _globalMaxRetryAttempts = maxRetryAttempts;
        foreach (var task in RenderTasks)
        {
            task.SetGlobalMaxRetryAttempts(maxRetryAttempts);
        }
    }

    public void SetVideoCodec(string codec)
    {
        _videoCodec = codec;
    }

    public void SetVideoQuality(string quality)
    {
        _videoQuality = quality;
    }

    public void SetBlenderPath(string blenderPath)
    {
        var previousPath = _blenderPath;

        _blenderProcessService?.Dispose();
        _blenderProcessService = null;

        _processService?.Dispose();
        _processService = null;

        _blenderPath = blenderPath;
        _blenderVideoService = null;

        if (!string.IsNullOrWhiteSpace(blenderPath))
        {
            _processService = new BlenderProcessService(blenderPath);
            Console.WriteLine($"[RenderQueueApplicationService] Blender path set: {blenderPath}");
        }

        if (!string.Equals(previousPath, blenderPath, StringComparison.Ordinal))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _workerHost.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderQueueApplicationService] Failed to shutdown worker on path change: {ex.Message}");
                }
            });
        }
    }

    public bool IsBlenderServiceReady()
    {
        return !string.IsNullOrWhiteSpace(_blenderPath) && File.Exists(_blenderPath);
    }

    public async Task AddTaskAsync()
    {
        if (!IsBlenderServiceReady())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPathRequired"]);
            return;
        }

        var blendFile = await SelectBlendFileAsync();
        if (string.IsNullOrWhiteSpace(blendFile))
        {
            return;
        }

        AddTaskToQueue(blendFile);
    }

    public async Task AddMultipleTasksAsync()
    {
        if (!IsBlenderServiceReady())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPathRequired"]);
            return;
        }

        var blendFiles = await SelectMultipleBlendFilesAsync();
        foreach (var blendFile in blendFiles)
        {
            AddTaskToQueue(blendFile);
        }
    }

    public void AddDroppedFiles(IEnumerable<IStorageItem> files)
    {
        if (!IsBlenderServiceReady())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPathRequired"]);
            return;
        }

        var blendFiles = files
            .OfType<IStorageFile>()
            .Where(file => file.Name.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.Path.LocalPath)
            .ToList();

        if (blendFiles.Count == 0)
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_DragBlendFiles"]);
            return;
        }

        foreach (var filePath in blendFiles)
        {
            AddTaskToQueue(filePath);
        }

        StatusMessageChanged?.Invoke(this,
            string.Format(Localizer.Localizer.Instance["Toast_TasksAddedSuccessfully"], blendFiles.Count));
    }

    public void RemoveSelectedTask(RenderTaskViewModel? selectedTask, Action<RenderTaskViewModel?> setSelectedTask)
    {
        if (selectedTask == null)
        {
            return;
        }

        RemoveTaskCore(selectedTask, selectedTask, setSelectedTask);
    }

    public void RemoveTask(RenderTaskViewModel? taskToRemove, RenderTaskViewModel? selectedTask,
        Action<RenderTaskViewModel?> setSelectedTask)
    {
        if (taskToRemove == null)
        {
            return;
        }

        if (!taskToRemove.IsPendingDeletion)
        {
            ClearPendingDeletionStates();
            taskToRemove.IsPendingDeletion = true;
            return;
        }

        RemoveTaskCore(taskToRemove, selectedTask, setSelectedTask);
    }

    public void RemoveAllTasks()
    {
        foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
        {
            _executionService.Stop(task);
        }

        foreach (var task in RenderTasks.ToList())
        {
            UnsubscribeFromTaskEvents(task);
            task.Dispose();
        }

        RenderTasks.Clear();
        CurrentRenderingTask = null;
        _pausedTask = null;
        _pausedFrame = 0;

        PublishSnapshot();
        StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_AllTasksCleared"]);
    }

    public void RequestRemoveAllTasksConfirmation()
    {
        ConfirmDialogRequested?.Invoke(this, new ConfirmDialogRequestedEventArgs(
            Localizer.Localizer.Instance["ConfirmClearAll_Title"],
            string.Format(Localizer.Localizer.Instance["ConfirmClearAll_Message"], RenderTasks.Count),
            Localizer.Localizer.Instance["ConfirmClearAll_Cancel"],
            Localizer.Localizer.Instance["ConfirmClearAll_Confirm"],
            RemoveAllTasks));
    }

    public void RemoveCompletedTasks()
    {
        var completedTasks = RenderTasks.Where(t =>
            t.Status == RenderTaskStatus.Completed ||
            t.Status == RenderTaskStatus.Failed ||
            t.Status == RenderTaskStatus.Cancelled).ToList();

        foreach (var task in completedTasks)
        {
            if (_pausedTask == task)
            {
                _pausedTask = null;
                _pausedFrame = 0;
            }

            UnsubscribeFromTaskEvents(task);
            RenderTasks.Remove(task);
            task.Dispose();
        }

        PublishSnapshot();
    }

    public async Task StartQueueAsync()
    {
        if (!Snapshot.CanStartQueue)
        {
            return;
        }

        if (!IsBlenderServiceReady())
        {
            QueueStatusChanged?.Invoke(this,
                new QueueStatusChangedEventArgs(Localizer.Localizer.Instance["Toast_BlenderPathRequired"]));
            return;
        }

        ClearPendingDeletionStates();

        foreach (var task in RenderTasks.Where(t => t.Enable && t.IsValid))
        {
            if (task.Status == RenderTaskStatus.Running)
            {
                _executionService.Stop(task);
            }

            task.Status = RenderTaskStatus.Pending;
            task.ResetProgress();
        }

        _queueState = QueueState.Running;
        _queueStatusText = "Queue_Running";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Started"));

        _recentFrameRenderTimes.Clear();
        _remainingTimeTimer.Start();
        PublishSnapshot();

        await StartNextAvailableTasksAsync();
    }

    public void StopQueue()
    {
        if (_queueState != QueueState.Running)
        {
            return;
        }

        _queueState = QueueState.Idle;
        _queueStatusText = "Queue_Stopped";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Stopped"));
        CurrentRenderingTask = null;
        _remainingTimeTimer.Stop();
        _recentFrameRenderTimes.Clear();
        _remainingTimeText = string.Empty;
        _pausedTask = null;
        _pausedFrame = 0;

        lock (_queueLock)
        {
            _runningTasks.Clear();
        }

        _ = Task.Run(() =>
        {
            foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
            {
                _executionService.Stop(task);
            }
        });

        PublishSnapshot();
    }

    public void PauseQueue()
    {
        if (_queueState != QueueState.Running || _activeTaskCount <= 0)
        {
            return;
        }

        if (CurrentRenderingTask is { Status: RenderTaskStatus.Running })
        {
            _pausedTask = CurrentRenderingTask;
            _pausedFrame = CurrentRenderingTask.CurrentFrame;
        }

        _queueState = QueueState.Paused;
        _queueStatusText = "Queue_Paused";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Paused"));
        _remainingTimeTimer.Stop();
        PublishSnapshot();

        _ = Task.Run(async () =>
        {
            foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
            {
                await _executionService.PauseAsync(task);
            }
        });
    }

    public async Task ResumeQueueAsync()
    {
        if (_queueState != QueueState.Paused)
        {
            return;
        }

        _queueState = QueueState.Running;
        _queueStatusText = "Queue_Running";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Resumed"));
        _remainingTimeTimer.Start();
        PublishSnapshot();
        await StartNextAvailableTasksAsync();
    }

    public void MoveTaskUp(RenderTaskViewModel? selectedTask)
    {
        if (selectedTask == null)
        {
            return;
        }

        var index = RenderTasks.IndexOf(selectedTask);
        if (index > 0)
        {
            RenderTasks.Move(index, index - 1);
        }
    }

    public void MoveTaskDown(RenderTaskViewModel? selectedTask)
    {
        if (selectedTask == null)
        {
            return;
        }

        var index = RenderTasks.IndexOf(selectedTask);
        if (index >= 0 && index < RenderTasks.Count - 1)
        {
            RenderTasks.Move(index, index + 1);
        }
    }

    public void MoveTaskToTop(RenderTaskViewModel? selectedTask)
    {
        if (selectedTask == null)
        {
            return;
        }

        var index = RenderTasks.IndexOf(selectedTask);
        if (index > 0)
        {
            RenderTasks.Move(index, 0);
        }
    }

    public void MoveTaskToBottom(RenderTaskViewModel? selectedTask)
    {
        if (selectedTask == null)
        {
            return;
        }

        var index = RenderTasks.IndexOf(selectedTask);
        if (index >= 0 && index < RenderTasks.Count - 1)
        {
            RenderTasks.Move(index, RenderTasks.Count - 1);
        }
    }

    public void CopyTask(RenderTaskViewModel? taskToCopy, Action<RenderTaskViewModel?> setSelectedTask)
    {
        if (taskToCopy == null)
        {
            return;
        }

        try
        {
            var newTask = new RenderTaskViewModel(
                taskToCopy.BlendFilePath,
                taskToCopy.StartFrame,
                taskToCopy.EndFrame,
                taskToCopy.AutoStart,
                taskToCopy.OverrideFrameRange)
            {
                Enable = taskToCopy.Enable
            };

            var savedOverrideScene = taskToCopy.OverrideScene;
            var savedSelectedSceneName = taskToCopy.SelectedSceneName;

            PrepareTask(newTask);
            RenderTasks.Add(newTask);
            SubscribeToTaskEvents(newTask);
            newTask.SetQueueRunningState(_queueState == QueueState.Running);
            setSelectedTask(newTask);

            if (IsBlenderServiceReady())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await newTask.LoadFilePropertiesAsync(_blenderPath!);
                        if (savedOverrideScene && !string.IsNullOrEmpty(savedSelectedSceneName))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                newTask.OverrideScene = savedOverrideScene;
                                newTask.SelectedSceneName = savedSelectedSceneName;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RenderQueueApplicationService] Failed to load copied task properties: {ex.Message}");
                    }
                });
            }

            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskCopied"], Path.GetFileName(taskToCopy.BlendFilePath)));
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskCopyFailed"], ex.Message));
        }
    }

    public Task<LocalSubmissionResponse> SubmitTaskAsync(LocalSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Filepath))
                {
                    return BuildSubmissionResponse(false, "Submission filepath is required.");
                }

                if (!File.Exists(request.Filepath))
                {
                    return BuildSubmissionResponse(false, $"Blend file does not exist: {request.Filepath}");
                }

                var taskInfo = new RenderTaskInfo
                {
                    Id = Guid.NewGuid(),
                    Filename = string.IsNullOrWhiteSpace(request.Filename)
                        ? Path.GetFileName(request.Filepath)
                        : request.Filename,
                    Filepath = request.Filepath,
                    StartFrame = request.FrameStart,
                    EndFrame = request.FrameEnd,
                    LastRenderedFrame = 0,
                    Enable = true,
                    Override = new OverrideData
                    {
                        OverrideFrameRange = request.OverrideFrameRange
                            ? new OverrideFrameRangeData
                            {
                                StartFrame = request.FrameStart,
                                EndFrame = request.FrameEnd
                            }
                            : null,
                        OverrideScene = string.IsNullOrWhiteSpace(request.SceneName)
                            ? null
                            : new OverrideSceneData
                            {
                                SceneName = request.SceneName
                            }
                    }
                };

                var task = new RenderTaskViewModel(taskInfo);
                PrepareTask(task);
                RenderTasks.Add(task);
                SubscribeToTaskEvents(task);
                task.SetQueueRunningState(_queueState == QueueState.Running);

                if (IsBlenderServiceReady())
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await task.LoadFilePropertiesAsync(_blenderPath!);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RenderQueueApplicationService] Failed to load submitted task properties: {ex.Message}");
                        }
                    }, cancellationToken);
                }

                StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPluginDetected"]);
                return new LocalSubmissionResponse
                {
                    Ok = true,
                    TaskId = task.Id.ToString("D"),
                    Message = $"Queued {Path.GetFileName(task.BlendFilePath)} successfully.",
                    QueueState = _queueState.ToString()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderQueueApplicationService] Failed to submit task locally: {ex.Message}");
                return BuildSubmissionResponse(false, ex.Message);
            }
        }).GetTask();
    }

    public Task<LocalSubmissionResponse> StartQueueFromSubmissionAsync(CancellationToken cancellationToken = default)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_queueState == QueueState.Running)
            {
                return BuildSubmissionResponse(true, "Queue is already running.");
            }

            if (!Snapshot.CanStartQueue)
            {
                return BuildSubmissionResponse(false, "Queue cannot be started in its current state.");
            }

            if (!IsBlenderServiceReady())
            {
                return BuildSubmissionResponse(false, "Blender is not configured or not ready.");
            }

            await StartQueueAsync();
            return BuildSubmissionResponse(true, "Queue started successfully.");
        });
    }

    public async Task LoadQueueDataAsync()
    {
        try
        {
            var appData = await _dataPersistenceService.LoadDataAsync();
            foreach (var taskData in appData.RenderQueue)
            {
                var task = new RenderTaskViewModel(taskData.RenderTask);
                PrepareTask(task);
                RenderTasks.Add(task);
                SubscribeToTaskEvents(task);
                task.SetQueueRunningState(_queueState == QueueState.Running);

                var savedOverrideScene = taskData.RenderTask.Override?.OverrideScene;
                if (IsBlenderServiceReady())
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                task.ScenePropertiesView.IsLoading = true;
                                task.ScenePropertiesView.LoadingMessage = "SceneProperties_LoadingFileProperties";
                            });

                            await task.LoadFilePropertiesAsync(_blenderPath!);

                            if (savedOverrideScene != null)
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    task.OverrideScene = true;
                                    task.SelectedSceneName = savedOverrideScene.SceneName;
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                task.ScenePropertiesView.IsLoading = false;
                                task.ScenePropertiesView.ErrorMessage = $"加载失败: {ex.Message}";
                            });
                        }
                    });
                }
            }

            PublishSnapshot();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueApplicationService] Error loading queue data: {ex.Message}");
        }
    }

    private void AddTaskToQueue(string blendFilePath)
    {
        try
        {
            var task = new RenderTaskViewModel(blendFilePath, 1, 1);
            PrepareTask(task);
            RenderTasks.Add(task);
            SubscribeToTaskEvents(task);
            task.SetQueueRunningState(_queueState == QueueState.Running);

            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskAdded"], Path.GetFileName(blendFilePath)));

            if (IsBlenderServiceReady())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await task.LoadFilePropertiesAsync(_blenderPath!);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RenderQueueApplicationService] Failed to load task properties: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskAddFailed"], ex.Message));
        }
    }

    private async Task StartNextAvailableTasksAsync()
    {
        if (_queueState != QueueState.Running)
        {
            return;
        }

        var runningTasks = RenderTasks.Where(t => t.Status == RenderTaskStatus.Running).ToList();
        foreach (var task in runningTasks)
        {
            _executionService.Stop(task);
        }

        await Task.Delay(100);

        RenderTaskViewModel? taskToStart;
        if (_pausedTask is { Enable: true, IsValid: true } && RenderTasks.Contains(_pausedTask))
        {
            taskToStart = _pausedTask;
        }
        else
        {
            if (_pausedTask != null && !RenderTasks.Contains(_pausedTask))
            {
                _pausedTask = null;
                _pausedFrame = 0;
            }

            taskToStart = RenderTasks.FirstOrDefault(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid);
        }

        if (taskToStart == null)
        {
            CurrentRenderingTask = null;
            PublishSnapshot();
            return;
        }

        CurrentRenderingTask = taskToStart;
        PublishSnapshot();

        var taskCopy = taskToStart;
        var runningTaskRef = new Task[1];
        lock (_queueLock)
        {
            runningTaskRef[0] = Task.Run(async () =>
            {
                try
                {
                    await _workerHost.EnsureReadyAsync(_blenderPath!, CancellationToken.None);

                    if (_pausedTask == taskCopy && _pausedFrame > 0)
                    {
                        await _executionService.ResumeAsync(taskCopy, _workerHost, _pausedFrame);
                        _pausedTask = null;
                        _pausedFrame = 0;
                    }
                    else
                    {
                        await _executionService.StartAsync(taskCopy, _workerHost);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderQueueApplicationService] Failed while starting queued task {Path.GetFileName(taskCopy.BlendFilePath)}: {ex}");
                }
                finally
                {
                    lock (_queueLock)
                    {
                        _runningTasks.RemoveAll(t => t == runningTaskRef[0]);
                    }

                    if (AutoStartNext && _queueState == QueueState.Running)
                    {
                        await StartNextAvailableTasksAsync();
                    }
                }
            });

            _runningTasks.Add(runningTaskRef[0]);
        }
    }

    private void SubscribeToTaskEvents(RenderTaskViewModel task)
    {
        task.StatusChanged += OnTaskStatusChanged;
        task.ProgressChanged += OnTaskProgressChanged;
        task.RefreshRequested += OnTaskRefreshRequested;
        task.EnableChanged += OnTaskEnableChanged;
        task.OverrideFrameRangeChanged += OnTaskStateMutated;
        task.OverrideSceneChanged += OnTaskStateMutated;
        task.SceneSelectionChanged += OnTaskStateMutated;
        task.FrameRangeChanged += OnTaskStateMutated;
    }

    private void UnsubscribeFromTaskEvents(RenderTaskViewModel task)
    {
        task.StatusChanged -= OnTaskStatusChanged;
        task.ProgressChanged -= OnTaskProgressChanged;
        task.RefreshRequested -= OnTaskRefreshRequested;
        task.EnableChanged -= OnTaskEnableChanged;
        task.OverrideFrameRangeChanged -= OnTaskStateMutated;
        task.OverrideSceneChanged -= OnTaskStateMutated;
        task.SceneSelectionChanged -= OnTaskStateMutated;
        task.FrameRangeChanged -= OnTaskStateMutated;
    }

    private void OnTaskStatusChanged(object? sender, RenderTaskStatusChangedEventArgs e)
    {
        PublishSnapshot();
        if (sender is RenderTaskViewModel task)
        {
            TaskCompleted?.Invoke(this, new TaskCompletedEventArgs(task, e.Status));
        }
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        if (e.CurrentFrame > 0)
        {
            RecordFrameCompletion(e.CurrentFrame, e.FrameRenderTime);
        }

        PublishSnapshot();
    }

    private async void OnTaskRefreshRequested(object? sender, EventArgs e)
    {
        if (sender is not RenderTaskViewModel task || !IsBlenderServiceReady())
        {
            return;
        }

        try
        {
            if (task.Status == RenderTaskStatus.Running)
            {
                _executionService.Stop(task);
            }

            await task.RefreshFilePropertiesAsync(_blenderPath!);
            PublishSnapshot();
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskReloaded"], Path.GetFileName(task.BlendFilePath)));
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskReloadFailed"], ex.Message));
        }
    }

    private void OnTaskEnableChanged(object? sender, EventArgs e)
    {
        AutoSaveQueueData();
        PublishSnapshot();
    }

    private void OnTaskStateMutated(object? sender, EventArgs e)
    {
        AutoSaveQueueData();
        PublishSnapshot();
    }

    private void PrepareTask(RenderTaskViewModel task)
    {
        task.SetGlobalRenderTimeout(_globalRenderTimeoutSeconds);
        task.SetGlobalMaxRetryAttempts(_globalMaxRetryAttempts);
        task.SetVideoCodec(_videoCodec);
        task.SetVideoQuality(_videoQuality);
        task.SetProcessService(_processService);
    }

    private void RemoveTaskCore(RenderTaskViewModel taskToRemove, RenderTaskViewModel? selectedTask,
        Action<RenderTaskViewModel?> setSelectedTask)
    {
        if (taskToRemove.Status == RenderTaskStatus.Running)
        {
            _executionService.Stop(taskToRemove);
        }

        var wasSelected = selectedTask == taskToRemove;
        var selectedIndex = wasSelected ? RenderTasks.IndexOf(taskToRemove) : -1;

        if (_pausedTask == taskToRemove)
        {
            _pausedTask = null;
            _pausedFrame = 0;
        }

        UnsubscribeFromTaskEvents(taskToRemove);
        RenderTasks.Remove(taskToRemove);
        taskToRemove.Dispose();

        if (wasSelected)
        {
            if (RenderTasks.Count > 0)
            {
                if (selectedIndex < RenderTasks.Count)
                {
                    setSelectedTask(RenderTasks[selectedIndex]);
                }
                else if (selectedIndex > 0)
                {
                    setSelectedTask(RenderTasks[selectedIndex - 1]);
                }
                else
                {
                    setSelectedTask(RenderTasks[0]);
                }
            }
            else
            {
                setSelectedTask(null);
            }
        }

        PublishSnapshot();
    }

    private void ClearPendingDeletionStates()
    {
        foreach (var task in RenderTasks)
        {
            task.IsPendingDeletion = false;
        }
    }

    private void RecordFrameCompletion(int frameNumber, TimeSpan frameRenderTime)
    {
        if (!(frameRenderTime.TotalSeconds > 0))
        {
            return;
        }

        _recentFrameRenderTimes.Enqueue(frameRenderTime);
        while (_recentFrameRenderTimes.Count > MaxRecentFrames)
        {
            _recentFrameRenderTimes.Dequeue();
        }
    }

    private void OnRemainingTimeTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateRemainingTime);
    }

    private void UpdateRemainingTime()
    {
        if (_queueState != QueueState.Running)
        {
            _remainingTimeText = string.Empty;
            PublishSnapshot();
            return;
        }

        var totalFrames = RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.RealTotalFrames);
        var completedFrameProgress = RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.RealTotalFrames * t.OverallProgress01);
        var remainingFrames = totalFrames - (int)completedFrameProgress;
        if (remainingFrames <= 0)
        {
            _remainingTimeText = string.Empty;
            PublishSnapshot();
            return;
        }

        if (_recentFrameRenderTimes.Count == 0)
        {
            _remainingTimeText = "Queue_Calculating";
            PublishSnapshot();
            return;
        }

        var averageRenderTime = _recentFrameRenderTimes.Average(rt => rt.TotalSeconds);
        var estimatedRemainingSeconds = remainingFrames * averageRenderTime;
        var formattedTime = $"{(int)TimeSpan.FromSeconds(estimatedRemainingSeconds).TotalHours:D2}:{TimeSpan.FromSeconds(estimatedRemainingSeconds).Minutes:D2}:{TimeSpan.FromSeconds(estimatedRemainingSeconds).Seconds:D2}";
        _remainingTimeText = $"Queue_RemainingTimeFormat:{formattedTime}";
        PublishSnapshot();
    }

    private Task<string> SelectBlendFileAsync()
    {
        var fileTypes = new[]
        {
            new FilePickerFileType("Blend Files") { Patterns = new[] { "*.blend" } }
        };

        return this.SelectFile("选择 Blend 文件", fileTypes).ContinueWith(t => t.Result ?? string.Empty);
    }

    private Task<IEnumerable<string>> SelectMultipleBlendFilesAsync()
    {
        var fileTypes = new[]
        {
            new FilePickerFileType("Blend Files") { Patterns = new[] { "*.blend" } }
        };

        return this.SelectFiles("选择多个 Blend 文件", fileTypes)
            .ContinueWith(t => t.Result ?? Enumerable.Empty<string>());
    }

    private async void AutoSaveQueueData()
    {
        try
        {
            await SaveQueueDataAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueApplicationService] Error in auto-save: {ex.Message}");
        }
    }

    private async Task SaveQueueDataAsync()
    {
        var appData = new AppData
        {
            RenderQueue = RenderTasks.Select(task => new RenderTaskData
            {
                RenderTask = CreateRenderTaskInfo(task)
            }).ToList()
        };

        await _dataPersistenceService.SaveDataAsync(appData);
    }

    private static RenderTaskInfo CreateRenderTaskInfo(RenderTaskViewModel task)
    {
        return new RenderTaskInfo
        {
            Id = task.Id,
            Filename = Path.GetFileName(task.BlendFilePath),
            Filepath = task.BlendFilePath,
            StartFrame = task.StartFrame,
            EndFrame = task.EndFrame,
            LastRenderedFrame = task.CurrentFrame,
            Enable = task.Enable,
            Override = new OverrideData
            {
                OverrideFrameRange = task.OverrideFrameRange
                    ? new OverrideFrameRangeData
                    {
                        StartFrame = task.StartFrame,
                        EndFrame = task.EndFrame
                    }
                    : null,
                OverrideScene = task.OverrideScene && !string.IsNullOrWhiteSpace(task.SelectedSceneName)
                    ? new OverrideSceneData
                    {
                        SceneName = task.SelectedSceneName
                    }
                    : null
            }
        };
    }

    private LocalSubmissionResponse BuildSubmissionResponse(bool ok, string message)
    {
        return new LocalSubmissionResponse
        {
            Ok = ok,
            Message = message,
            QueueState = _queueState.ToString()
        };
    }

    private void PublishSnapshot()
    {
        _activeTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Running);
        _completedTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Completed);
        _failedTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Failed || t.Status == RenderTaskStatus.Cancelled);

        switch (_queueState)
        {
            case QueueState.Running:
                if (_activeTaskCount > 0)
                {
                    _queueStatusText = $"Queue_RunningWithTasks:{_activeTaskCount}";
                }
                else if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid))
                {
                    _queueStatusText = "Queue_Waiting";
                }
                else if (RenderTasks.Where(t => t.Enable && t.IsValid).All(t =>
                             t.Status == RenderTaskStatus.Completed ||
                             t.Status == RenderTaskStatus.Failed ||
                             t.Status == RenderTaskStatus.Cancelled))
                {
                    _queueStatusText = "Queue_Completed";
                    _queueState = QueueState.Completed;
                    _ = HandlePostRenderBehaviorAsync();
                }
                else
                {
                    _queueStatusText = "Queue_Running";
                }
                break;
            case QueueState.Idle:
                if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid))
                {
                    _queueStatusText = "Queue_Idle";
                }
                else if (RenderTasks.Where(t => t is { Enable: true, IsValid: true }).Any(t =>
                             t.Status is RenderTaskStatus.Completed or RenderTaskStatus.Failed or RenderTaskStatus.Cancelled))
                {
                    _queueStatusText = "Queue_Completed";
                }
                else
                {
                    _queueStatusText = "Queue_Empty";
                }
                break;
            case QueueState.Completed:
                _queueStatusText = "Queue_Completed";
                break;
            case QueueState.Paused:
                _queueStatusText = "Queue_Paused";
                break;
            case QueueState.Error:
                _queueStatusText = "Queue_Error";
                break;
        }

        foreach (var task in RenderTasks)
        {
            task.SetQueueRunningState(_queueState == QueueState.Running);
        }

        Snapshot = BuildSnapshot();
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private RenderQueueSnapshot BuildSnapshot()
    {
        var totalFrames = RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.RealTotalFrames);
        var completedFrameProgress = RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.RealTotalFrames * t.OverallProgress01);
        var overallProgress = totalFrames > 0 ? completedFrameProgress / totalFrames : 0.0;

        return new RenderQueueSnapshot
        {
            State = _queueState switch
            {
                QueueState.Running => QueueExecutionState.Running,
                QueueState.Paused => QueueExecutionState.Paused,
                QueueState.Completed => QueueExecutionState.Completed,
                QueueState.Error => QueueExecutionState.Error,
                _ => QueueExecutionState.Idle
            },
            CurrentTaskId = CurrentRenderingTask?.Id,
            ActiveTaskCount = _activeTaskCount,
            CompletedTaskCount = _completedTaskCount,
            FailedTaskCount = _failedTaskCount,
            TotalFrames = totalFrames,
            CompletedFrameProgress = completedFrameProgress,
            OverallProgress01 = overallProgress,
            QueueStatusText = _queueStatusText,
            RemainingTimeText = _remainingTimeText,
            AutoStartNext = AutoStartNext,
            PostRenderBehavior = PostRenderBehavior,
            CanStartQueue = RenderTasks.Count > 0 && (_queueState is QueueState.Idle or QueueState.Completed) && RenderTasks.Any(t => t is { Enable: true, IsValid: true }),
            CanStopQueue = _queueState == QueueState.Running,
            CanPauseQueue = _queueState == QueueState.Running && _activeTaskCount > 0,
            CanResumeQueue = _queueState == QueueState.Paused,
            CanClearTasks = _queueState is QueueState.Completed or QueueState.Idle,
            Tasks = RenderTasks.Select(BuildTaskSnapshot).ToList()
        };
    }

    private static RenderTaskSnapshot BuildTaskSnapshot(RenderTaskViewModel task)
    {
        return new RenderTaskSnapshot
        {
            TaskId = task.Id,
            BlendFilePath = task.BlendFilePath,
            BlendFileName = task.BlendFileName,
            Enabled = task.Enable,
            IsValid = task.IsValid,
            State = task.Status switch
            {
                RenderTaskStatus.Running => RenderTaskExecutionState.Running,
                RenderTaskStatus.Paused => RenderTaskExecutionState.Paused,
                RenderTaskStatus.Completed => RenderTaskExecutionState.Completed,
                RenderTaskStatus.Failed => RenderTaskExecutionState.Failed,
                RenderTaskStatus.Cancelled => RenderTaskExecutionState.Cancelled,
                _ => RenderTaskExecutionState.Pending
            },
            CurrentFrame = task.CurrentFrame,
            CompletedFrames = task.CompletedFrames,
            TotalFrames = task.RealTotalFrames,
            CurrentFrameProgress01 = task.Progress01,
            OverallProgress01 = task.OverallProgress01,
            SampleText = task.SampleText,
            StatusDetailText = task.StatusDetailText,
            OutputPath = task.SavedPath,
            PreviewPath = task.RenderedImagePath,
            OverrideSceneName = task.SelectedSceneName,
            OverrideFrameRange = task.OverrideFrameRange,
            RealStartFrame = task.RealStartFrame,
            RealEndFrame = task.RealEndFrame,
            Duration = task.Duration
        };
    }

    private async Task HandlePostRenderBehaviorAsync()
    {
        if (PostRenderBehavior == PostRenderBehavior.None)
        {
            return;
        }

        try
        {
            var actionType = PostRenderBehavior switch
            {
                PostRenderBehavior.Shutdown => Localizer.Localizer.Instance["SystemControl_Shutdown"],
                PostRenderBehavior.Restart => Localizer.Localizer.Instance["SystemControl_Restart"],
                _ => string.Empty
            };

            var success = PostRenderBehavior switch
            {
                PostRenderBehavior.Shutdown => await SystemControlHelper.ShutdownAsync(60, CancellationToken.None),
                PostRenderBehavior.Restart => await SystemControlHelper.RestartAsync(60, CancellationToken.None),
                _ => false
            };

            if (!success)
            {
                StatusMessageChanged?.Invoke(this,
                    string.Format(Localizer.Localizer.Instance["SystemControl_ActionFailed"], actionType));
            }
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["SystemControl_ActionError"], ex.Message));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopQueue();
        _remainingTimeTimer.Stop();
        _remainingTimeTimer.Dispose();
        _processService?.Dispose();
        _blenderProcessService?.Dispose();

        try
        {
            _workerHost.Dispose();
        }
        catch
        {
            // ignored
        }

        lock (_queueLock)
        {
            _runningTasks.Clear();
        }

        foreach (var task in RenderTasks.ToList())
        {
            UnsubscribeFromTaskEvents(task);
            task.Dispose();
        }

        RenderTasks.Clear();
    }
}
