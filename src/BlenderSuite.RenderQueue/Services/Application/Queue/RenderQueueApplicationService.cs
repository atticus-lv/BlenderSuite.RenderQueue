using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Threading;
using BlenderSuite.RenderQueue.Extensions;
using BlenderSuite.RenderQueue.Helpers;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using BlenderSuite.RenderQueue.Services.Business.Persistence;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Services.Application.Queue;

public sealed partial class RenderQueueApplicationService : IRenderQueueApplicationService
{
    private readonly IBlenderWorkerHost _workerHost;
    private readonly IRenderTaskExecutionService _executionService;
    private readonly IDataPersistenceService _dataPersistenceService;
    private readonly IRenderLogService _logService;
    private readonly IRenderTaskFactory _taskFactory;
    private readonly SemaphoreSlim _taskPropertiesLoadLimiter = new(1, 1);
    private readonly SemaphoreSlim _schedulerLock = new(1, 1);
    private readonly List<Task> _runningTasks = [];
    private readonly HashSet<Guid> _scheduledTaskIds = [];
    private readonly object _queueLock = new();
    private readonly object _saveStateLock = new();
    private readonly Queue<TimeSpan> _recentFrameRenderTimes = new();
    private const int MaxRecentFrames = 3;
    private readonly System.Timers.Timer _remainingTimeTimer;

    private RenderTaskViewModel? _pausedTask;
    private int _pausedFrame;
    private BlenderProcessService? _blenderProcessService;
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
    private Guid _batchId = Guid.NewGuid();
    private string _batchName = string.Empty;
    private DateTimeOffset _batchCreatedAt = DateTimeOffset.UtcNow;
    private bool _savePending;
    private bool _saveWorkerRunning;
    private bool _disposed;

    public RenderQueueApplicationService(
        IBlenderWorkerHost workerHost,
        IRenderTaskExecutionService executionService,
        IDataPersistenceService dataPersistenceService,
        IRenderLogService logService,
        IRenderTaskFactory taskFactory)
    {
        _workerHost = workerHost;
        _executionService = executionService;
        _dataPersistenceService = dataPersistenceService;
        _logService = logService;
        _taskFactory = taskFactory;
        _scheduler = new RenderQueueScheduler(this);
        _persistenceCoordinator = new RenderQueuePersistenceCoordinator(this);
        _snapshotFactory = new RenderQueueSnapshotFactory(this);

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
        if (!string.IsNullOrWhiteSpace(blenderPath))
        {
            _processService = new BlenderProcessService(blenderPath, _logService);
            _logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, $"Blender path set: {blenderPath}", source: "RenderQueueApplicationService", metadata: RenderLogMetadata.Diagnostic());
        }

        foreach (var task in RenderTasks)
        {
            task.SetProcessService(_processService);
        }

        if (IsBlenderServiceReady())
        {
            foreach (var task in RenderTasks.Where(ShouldBackfillTaskProperties))
            {
                LoadTaskPropertiesWithLimitAsync(
                    task,
                    onError: ex => _logService.Write(RenderLogLevel.Error, RenderLogScope.Queue, $"Failed to backfill task properties: {ex.Message}", source: "RenderQueueApplicationService", metadata: RenderLogMetadata.Diagnostic()))
                    .FireAndForget(
                        _logService,
                        nameof(RenderQueueApplicationService),
                        RenderLogScope.Task,
                        "后台回填任务属性失败。");
            }
        }

        if (!string.Equals(previousPath, blenderPath, StringComparison.Ordinal))
        {
            _logService.Write(
                string.IsNullOrWhiteSpace(blenderPath) ? RenderLogLevel.Warning : RenderLogLevel.Info,
                RenderLogScope.System,
                string.IsNullOrWhiteSpace(blenderPath)
                    ? "已清除 Blender 路径。"
                    : $"已更新 Blender 路径: {blenderPath}",
                source: nameof(RenderQueueApplicationService));
            Task.Run(async () =>
            {
                try
                {
                    await _workerHost.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    _logService.Write(RenderLogLevel.Error, RenderLogScope.Queue, $"Failed to shutdown worker on path change: {ex.Message}", source: "RenderQueueApplicationService", metadata: RenderLogMetadata.Diagnostic());
                    _logService.Write(RenderLogLevel.Warning, RenderLogScope.Worker, $"切换 Blender 路径时关闭 worker 失败: {ex.Message}", source: nameof(RenderQueueApplicationService));
                }
            }).FireAndForget(
                _logService,
                nameof(RenderQueueApplicationService),
                RenderLogScope.Worker,
                "切换 Blender 路径时后台关闭 worker 任务失败。");
        }
    }

    public bool IsBlenderServiceReady()
    {
        return !string.IsNullOrWhiteSpace(_blenderPath) && File.Exists(_blenderPath);
    }

    public void AddBlendFiles(IEnumerable<string> filePaths)
    {
        AddBlendFilesCore(filePaths, showNoFilesMessage: false, showSummaryMessage: false);
    }

    public void AddDroppedFiles(IEnumerable<string> filePaths)
    {
        AddBlendFilesCore(filePaths, showNoFilesMessage: true, showSummaryMessage: true);
    }

    private void AddBlendFilesCore(IEnumerable<string> filePaths, bool showNoFilesMessage, bool showSummaryMessage)
    {
        if (!IsBlenderServiceReady())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPathRequired"]);
            return;
        }

        var blendFiles = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Where(path => path.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (blendFiles.Count == 0)
        {
            if (showNoFilesMessage)
            {
                StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_DragBlendFiles"]);
            }

            return;
        }

        foreach (var filePath in blendFiles)
        {
            AddTaskToQueue(filePath);
        }

        if (showSummaryMessage)
        {
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TasksAddedSuccessfully"], blendFiles.Count));
        }
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
        _logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, $"清空全部任务，数量: {RenderTasks.Count}", source: nameof(RenderQueueApplicationService));
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
        _logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "队列开始运行。", source: nameof(RenderQueueApplicationService));
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
        _logService.Write(RenderLogLevel.Warning, RenderLogScope.Queue, "队列已停止。", source: nameof(RenderQueueApplicationService));
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

        Task.Run(() =>
        {
            foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
            {
                _executionService.Stop(task);
            }
        }).FireAndForget(
            _logService,
            nameof(RenderQueueApplicationService),
            RenderLogScope.Queue,
            "停止队列后台任务失败。");

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
        _logService.Write(RenderLogLevel.Warning, RenderLogScope.Queue, "队列已暂停。", source: nameof(RenderQueueApplicationService));
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Paused"));
        _remainingTimeTimer.Stop();
        PublishSnapshot();

        Task.Run(async () =>
        {
            foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
            {
                await _executionService.PauseAsync(task);
            }
        }).FireAndForget(
            _logService,
            nameof(RenderQueueApplicationService),
            RenderLogScope.Queue,
            "暂停队列后台任务失败。");
    }

    public async Task ResumeQueueAsync()
    {
        if (_queueState != QueueState.Paused)
        {
            return;
        }

        _queueState = QueueState.Running;
        _queueStatusText = "Queue_Running";
        _logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "队列已恢复运行。", source: nameof(RenderQueueApplicationService));
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
            var newTask = _taskFactory.Create(
                taskToCopy.BlendFilePath,
                taskToCopy.StartFrame,
                taskToCopy.EndFrame,
                taskToCopy.AutoStart,
                taskToCopy.OverrideFrameRange,
                CreateTaskFactoryOptions());
            newTask.Enable = taskToCopy.Enable;

            var savedOverrideScene = taskToCopy.OverrideScene;
            var savedSelectedSceneName = taskToCopy.SelectedSceneName;

            RenderTasks.Add(newTask);
            SubscribeToTaskEvents(newTask);
            setSelectedTask(newTask);
            WriteTaskEvent(newTask, RenderLogScope.Task, $"任务已复制入队: {Path.GetFileName(newTask.BlendFilePath)}");

            if (IsBlenderServiceReady())
            {
                LoadTaskPropertiesWithLimitAsync(
                    newTask,
                    postLoadAsync: savedOverrideScene && !string.IsNullOrEmpty(savedSelectedSceneName)
                        ? () => Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            newTask.OverrideScene = savedOverrideScene;
                            newTask.SelectedSceneName = savedSelectedSceneName;
                        }).GetTask()
                        : null,
                    onError: ex => _logService.Write(RenderLogLevel.Error, RenderLogScope.Queue, $"Failed to load copied task properties: {ex.Message}", source: "RenderQueueApplicationService"))
                    .FireAndForget(
                        _logService,
                        nameof(RenderQueueApplicationService),
                        RenderLogScope.Task,
                        "后台加载复制任务属性失败。");
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

    public async Task LoadQueueDataAsync()
    {
        var operation = _logService.BeginOperation(
            RenderLogScope.Recovery,
            "RestoreQueueData",
            nameof(RenderQueueApplicationService),
            "开始加载持久化队列数据。");
        try
        {
            var appData = await _dataPersistenceService.LoadDataAsync();
            ApplyBatchMetadata(appData);
            operation.Detail(
                $"读取到持久化任务数: {appData.RenderQueue.Count}",
                metadata: new Dictionary<string, string>
                {
                    ["persisted_task_count"] = appData.RenderQueue.Count.ToString()
                });
            var existingTaskIds = RenderTasks.Select(t => t.Id).ToHashSet();
            var restoredCount = 0;
            var skippedCount = 0;
            foreach (var taskData in appData.RenderQueue)
            {
                var persistedTask = taskData.RenderTask;
                if (persistedTask.Id != Guid.Empty && existingTaskIds.Contains(persistedTask.Id))
                {
                    skippedCount++;
                    operation.Detail(
                        $"跳过已存在的持久化任务: {Path.GetFileName(persistedTask.Filepath)}",
                        metadata: new Dictionary<string, string>
                        {
                            ["task_id"] = persistedTask.Id.ToString("D"),
                            ["blend_file"] = persistedTask.Filepath
                        });
                    continue;
                }

                var task = _taskFactory.Create(persistedTask, CreateTaskFactoryOptions());
                RenderTasks.Add(task);
                SubscribeToTaskEvents(task);
                WriteTaskEvent(task, RenderLogScope.Recovery, "已从持久化数据恢复任务。");
                existingTaskIds.Add(task.Id);
                restoredCount++;

                var savedOverrideScene = taskData.RenderTask.Override?.OverrideScene;
                if (IsBlenderServiceReady())
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        task.ScenePropertiesView.IsLoading = true;
                        task.ScenePropertiesView.LoadingMessage = "SceneProperties_LoadingFileProperties";
                    });

                    LoadTaskPropertiesWithLimitAsync(
                        task,
                        postLoadAsync: savedOverrideScene != null
                            ? () => Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                task.OverrideScene = true;
                                task.SelectedSceneName = savedOverrideScene.SceneName;
                            }).GetTask()
                            : null,
                        onError: ex => Dispatcher.UIThread.Post(() =>
                        {
                            task.ScenePropertiesView.IsLoading = false;
                            task.ScenePropertiesView.ErrorMessage = $"加载失败: {ex.Message}";
                        }))
                        .FireAndForget(
                            _logService,
                            nameof(RenderQueueApplicationService),
                            RenderLogScope.Recovery,
                            "后台加载恢复任务属性失败。");
                }
            }

            PublishSnapshot();
            operation.Complete(
                $"持久化队列数据加载完成，恢复 {restoredCount} 个任务，当前任务总数: {RenderTasks.Count}",
                metadata: new Dictionary<string, string>
                {
                    ["restored_task_count"] = restoredCount.ToString(),
                    ["skipped_task_count"] = skippedCount.ToString(),
                    ["total_task_count"] = RenderTasks.Count.ToString()
                });
        }
        catch (Exception ex)
        {
            operation.Detail($"Error loading queue data: {ex.Message}", RenderLogLevel.Error);
            operation.Fail($"加载持久化队列数据失败: {ex.Message}");
        }
    }

    private void ApplyBatchMetadata(AppData appData)
    {
        _batchId = appData.BatchId == Guid.Empty ? Guid.NewGuid() : appData.BatchId;
        _batchName = appData.BatchName ?? string.Empty;
        _batchCreatedAt = appData.CreatedAt == default ? DateTimeOffset.UtcNow : appData.CreatedAt;
    }

    private void AddTaskToQueue(string blendFilePath)
    {
        try
        {
            var task = _taskFactory.Create(blendFilePath, 1, 1, options: CreateTaskFactoryOptions());
            RenderTasks.Add(task);
            SubscribeToTaskEvents(task);
            WriteTaskEvent(task, RenderLogScope.Task, $"任务已入队: {Path.GetFileName(blendFilePath)}");

            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskAdded"], Path.GetFileName(blendFilePath)));

            if (IsBlenderServiceReady())
            {
                LoadTaskPropertiesWithLimitAsync(
                    task,
                    onError: ex => _logService.Write(RenderLogLevel.Error, RenderLogScope.Queue, $"Failed to load task properties: {ex.Message}", source: "RenderQueueApplicationService", metadata: RenderLogMetadata.Diagnostic()))
                    .FireAndForget(
                        _logService,
                        nameof(RenderQueueApplicationService),
                        RenderLogScope.Task,
                        "后台加载任务属性失败。");
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
        await _scheduler.StartNextAvailableTasksAsync();
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
            _logService.Write(
                RenderLogLevel.Error,
                RenderLogScope.Task,
                $"刷新任务文件属性失败: {ex}",
                task.Id,
                task.BlendFilePath,
                nameof(RenderQueueApplicationService));
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

    private RenderTaskFactoryOptions CreateTaskFactoryOptions()
    {
        return new RenderTaskFactoryOptions
        {
            GlobalRenderTimeoutSeconds = _globalRenderTimeoutSeconds,
            GlobalMaxRetryAttempts = _globalMaxRetryAttempts,
            VideoCodec = _videoCodec,
            VideoQuality = _videoQuality,
            ProcessService = _processService,
            IsQueueRunning = _queueState == QueueState.Running
        };
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
        taskToRemove.DetachLogService();
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

    private void AutoSaveQueueData()
    {
        _persistenceCoordinator.AutoSaveQueueData();
    }

    private async Task RunAutoSaveLoopAsync()
    {
        await _persistenceCoordinator.RunAutoSaveLoopAsync();
    }

    private AppData BuildAppDataSnapshot()
    {
        return _persistenceCoordinator.BuildAppDataSnapshot();
    }

    private static bool ShouldBackfillTaskProperties(RenderTaskViewModel task)
    {
        return task.IsValid &&
               !string.IsNullOrWhiteSpace(task.BlendFilePath) &&
               File.Exists(task.BlendFilePath) &&
               !task.ScenePropertiesView.IsLoading &&
               !task.ScenePropertiesView.SelectedSceneProperties.IsLoaded;
    }

    private async Task LoadTaskPropertiesWithLimitAsync(
        RenderTaskViewModel task,
        Func<Task>? postLoadAsync = null,
        Action<Exception>? onError = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _taskPropertiesLoadLimiter.WaitAsync(cancellationToken);
            try
            {
                if (_disposed || !IsBlenderServiceReady())
                {
                    return;
                }

                await task.LoadFilePropertiesAsync(_blenderPath!);
                if (postLoadAsync != null)
                {
                    await postLoadAsync();
                }
            }
            finally
            {
                _taskPropertiesLoadLimiter.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
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
                    _logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "队列已完成。", source: nameof(RenderQueueApplicationService));
                    HandlePostRenderBehaviorAsync().FireAndForget(
                        _logService,
                        nameof(RenderQueueApplicationService),
                        RenderLogScope.Queue,
                        "队列完成后的系统行为后台任务失败。");
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
        return _snapshotFactory.BuildSnapshot();
    }

    private static RenderTaskSnapshot BuildTaskSnapshot(RenderTaskViewModel task)
    {
        return RenderQueueSnapshotFactory.BuildTaskSnapshot(task);
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
            task.DetachLogService();
            task.Dispose();
        }

        RenderTasks.Clear();
    }

    private void WriteTaskEvent(
        RenderTaskViewModel task,
        RenderLogScope scope,
        string message,
        RenderLogLevel level = RenderLogLevel.Info,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        _logService.Write(level, scope, message, task.Id, task.BlendFilePath, nameof(RenderQueueApplicationService), metadata);
    }
}
