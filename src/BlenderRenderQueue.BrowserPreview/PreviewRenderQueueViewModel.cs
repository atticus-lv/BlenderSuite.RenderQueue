using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.ViewModels.DesignTime;

namespace BlenderRenderQueue.BrowserPreview;

public sealed class PreviewRootViewModel
{
    public PreviewRootViewModel()
    {
        RenderQueue = new PreviewRenderQueueViewModel();
        Settings = CreateSettingsViewModel();
    }

    public PreviewRenderQueueViewModel RenderQueue { get; }
    public SettingsViewModel Settings { get; }

    private static SettingsViewModel CreateSettingsViewModel()
    {
        var settings = new SettingsViewModel(
            new PreviewSettingsPersistenceService(),
            new PreviewBlenderValidationService(),
            new PreviewRenderLogService());

        var blender43 = new BlenderExecutable
        {
            Path = "/Applications/Blender.app/Contents/MacOS/Blender",
            Version = "4.3.2",
            Branch = "main",
            Hash = "b4a4f1c8742f",
            Platform = "macOS arm64",
            Type = "Release",
            BuildDate = new DateTime(2026, 5, 28),
            BuildTime = "14:22:08",
            CommitDate = new DateTime(2026, 5, 27),
            CommitTime = "21:48:11",
            IsValid = true,
            LastValidated = new DateTime(2026, 6, 4, 19, 49, 8)
        };

        var blender44 = new BlenderExecutable
        {
            Path = "/Applications/Blender 4.4.app/Contents/MacOS/Blender",
            Version = "4.4.0 Alpha",
            Branch = "experimental",
            Hash = "e81c2da94710",
            Platform = "macOS arm64",
            Type = "Alpha",
            BuildDate = new DateTime(2026, 6, 2),
            BuildTime = "09:17:31",
            CommitDate = new DateTime(2026, 6, 1),
            CommitTime = "22:04:56",
            IsValid = true,
            LastValidated = new DateTime(2026, 6, 4, 19, 52, 12)
        };

        settings.BlenderExecutables.Add(blender43);
        settings.BlenderExecutables.Add(blender44);
        settings.DefaultRenderTimeoutSeconds = 600;
        settings.MaxRetryAttempts = 3;
        settings.VideoCodec = VideoCodecOption.H264;
        settings.VideoQuality = VideoQualityOption.PerceptualLossless;
        settings.Language = LanguageOption.Default;
        settings.BaseTheme = ThemeOption.FindByValue("Dark") ?? ThemeOption.Default;
        settings.HardwareAcceleration = true;
        settings.SelectedBlenderExecutable = blender43;
        settings.HasUnsavedChanges = false;
        settings.HardwareAccelerationChanged = false;

        return settings;
    }
}

public sealed class PreviewRenderQueueViewModel : RenderQueueViewModel
{
    public PreviewRenderQueueViewModel()
        : this(new PreviewQueueApplicationService())
    {
    }

    private PreviewRenderQueueViewModel(PreviewQueueApplicationService queueService)
        : base(queueService, new PreviewRenderLogService())
    {
        queueService.LoadDemoState();
        SelectedTask = queueService.RenderTasks.Count > 0 ? queueService.RenderTasks[0] : null;
    }
}

internal sealed class PreviewQueueApplicationService : IRenderQueueApplicationService
{
    private readonly DispatcherTimer _demoTimer;
    private readonly Bitmap[] _mockFrames;
    private int _demoFrame = 115;
    private int _demoSample = 116;
    private int _demoTick;
    private int _addedTaskIndex = 1;
    private QueueExecutionState _queueState = QueueExecutionState.Idle;

    public PreviewQueueApplicationService()
    {
        _mockFrames = CreateMockFrames();
        _demoTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _demoTimer.Tick += OnDemoTimerTick;
    }

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

    public void LoadDemoState()
    {
        var untitled = CreateTask("Untitled.blend", "Scene", 1, 250, RenderTaskStatus.Pending, 0, 0, 0);
        var interior = CreateTask("Interior_Light.blend", "Camera", 1, 180, RenderTaskStatus.Running, 115, 115d / 180d, 0.58);
        var logo = CreateTask("Logo_Reveal.blend", "Main", 1, 96, RenderTaskStatus.Pending, 0, 0, 0);
        logo.Enable = false;

        RenderTasks.Clear();
        RenderTasks.Add(untitled);
        RenderTasks.Add(interior);
        RenderTasks.Add(logo);

        CurrentRenderingTask = interior;
        _queueState = QueueExecutionState.Running;
        PublishSnapshot();
        _demoTimer.Start();
    }

    private void OnDemoTimerTick(object? sender, EventArgs e)
    {
        if (_queueState != QueueExecutionState.Running)
        {
            return;
        }

        if (CurrentRenderingTask is not { } task)
        {
            StartNextEnabledTask(resetProgress: false);
            return;
        }

        _demoFrame += 2;
        _demoSample = _demoSample >= 250 ? 36 : _demoSample + 31;
        _demoTick++;

        if (_demoFrame > task.RealEndFrame)
        {
            _demoFrame = task.RealStartFrame + 18;
        }

        var completedFrames = Math.Max(0, _demoFrame - task.RealStartFrame);
        var sampleProgress = Math.Clamp(_demoSample / 250d, 0, 1);
        var taskProgress = Math.Clamp((completedFrames + sampleProgress) / Math.Max(1, task.RealTotalFrames), 0, 1);

        task.Status = RenderTaskStatus.Running;
        task.CurrentFrame = _demoFrame;
        task.CompletedFrames = completedFrames;
        task.Progress01 = sampleProgress;
        task.OverallProgress01 = taskProgress;
        task.SampleText = $"{_demoSample}/250";
        task.Engine = "Cycles";
        task.RenderedImage = _mockFrames[_demoTick % _mockFrames.Length];
        task.RenderedImagePath = $"/render/output/frame_{_demoFrame:0000}.png";
        task.SavedPath = task.RenderedImagePath;
        task.HasRenderedImage = true;

        PublishSnapshot();
    }

    private RenderQueueSnapshot BuildSnapshot()
    {
        var enabledTasks = RenderTasks.Where(task => task is { Enable: true, IsValid: true }).ToArray();
        var totalFrames = enabledTasks.Sum(task => task.RealTotalFrames);
        var completedFrameProgress = enabledTasks.Sum(task => task.RealTotalFrames * task.OverallProgress01);
        var overallProgress = totalFrames > 0 ? Math.Clamp(completedFrameProgress / totalFrames, 0, 1) : 0;
        var remainingSeconds = Math.Max(90, (int)Math.Round((1 - overallProgress) * 1020));
        var isActive = _queueState is QueueExecutionState.Running or QueueExecutionState.Paused &&
                       CurrentRenderingTask != null;

        return new RenderQueueSnapshot
        {
            State = _queueState,
            CurrentTaskId = isActive ? CurrentRenderingTask?.Id : null,
            ActiveTaskCount = isActive ? 1 : 0,
            CompletedTaskCount = RenderTasks.Count(task => task.Status == RenderTaskStatus.Completed),
            FailedTaskCount = 0,
            TotalFrames = totalFrames,
            CompletedFrameProgress = completedFrameProgress,
            OverallProgress01 = overallProgress,
            QueueStatusText = _queueState switch
            {
                QueueExecutionState.Running => "Queue_Running",
                QueueExecutionState.Paused => "Queue_Paused",
                QueueExecutionState.Completed => "Queue_Completed",
                QueueExecutionState.Error => "Queue_Error",
                _ => "Queue_Idle"
            },
            RemainingTimeText = _queueState is QueueExecutionState.Running or QueueExecutionState.Paused
                ? $"Queue_RemainingTimeFormat:{TimeSpan.FromSeconds(remainingSeconds):hh\\:mm\\:ss}"
                : string.Empty,
            AutoStartNext = AutoStartNext,
            PostRenderBehavior = PostRenderBehavior,
            CanStartQueue = _queueState is QueueExecutionState.Idle or QueueExecutionState.Completed &&
                            enabledTasks.Length > 0,
            CanStopQueue = _queueState is QueueExecutionState.Running or QueueExecutionState.Paused,
            CanPauseQueue = _queueState == QueueExecutionState.Running && CurrentRenderingTask != null,
            CanResumeQueue = _queueState == QueueExecutionState.Paused && CurrentRenderingTask != null,
            CanClearTasks = _queueState is QueueExecutionState.Idle or QueueExecutionState.Completed,
            Tasks = RenderTasks.Select(BuildTaskSnapshot).ToList()
        };
    }

    private void PublishSnapshot()
    {
        Snapshot = BuildSnapshot();
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private RenderTaskViewModel? StartNextEnabledTask(bool resetProgress)
    {
        var nextTask = RenderTasks.FirstOrDefault(task => task is { Enable: true, IsValid: true } &&
                                                          task.Status != RenderTaskStatus.Completed);
        if (nextTask == null)
        {
            CurrentRenderingTask = null;
            _queueState = QueueExecutionState.Completed;
            _demoTimer.Stop();
            PublishSnapshot();
            return null;
        }

        CurrentRenderingTask = nextTask;
        if (resetProgress || nextTask.Status is RenderTaskStatus.Cancelled or RenderTaskStatus.Completed)
        {
            ResetTaskProgress(nextTask);
        }

        _demoFrame = Math.Max(nextTask.RealStartFrame, nextTask.CurrentFrame);
        _demoSample = 36;
        nextTask.Status = RenderTaskStatus.Running;
        nextTask.Engine = "Cycles";
        nextTask.HasRenderedImage = true;
        nextTask.RenderedImage = _mockFrames[_demoTick % _mockFrames.Length];
        nextTask.RenderedImagePath = $"/render/output/frame_{_demoFrame:0000}.png";
        nextTask.SavedPath = nextTask.RenderedImagePath;
        _queueState = QueueExecutionState.Running;
        _demoTimer.Start();
        PublishSnapshot();
        return nextTask;
    }

    private static void ResetTaskProgress(RenderTaskViewModel task)
    {
        task.Status = RenderTaskStatus.Pending;
        task.CurrentFrame = task.RealStartFrame;
        task.CompletedFrames = 0;
        task.Progress01 = 0;
        task.OverallProgress01 = 0;
        task.SampleText = "0/250";
        task.StatusDetailText = string.Empty;
        task.HasRenderedImage = false;
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

    private static Bitmap[] CreateMockFrames()
    {
        return
        [
            CreateMockFrame(0, 42, 119, 190),
            CreateMockFrame(1, 231, 145, 44),
            CreateMockFrame(2, 60, 196, 130),
            CreateMockFrame(3, 154, 111, 235)
        ];
    }

    private static unsafe Bitmap CreateMockFrame(int phase, byte accentR, byte accentG, byte accentB)
    {
        const int width = 180;
        const int height = 120;
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var locked = bitmap.Lock();
        var ptr = (byte*)locked.Address;
        var stride = locked.RowBytes;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var diagonal = (x + y + phase * 34) % 92;
                var highlight = diagonal < 18 ? 64 : 0;
                var glow = Math.Max(0, 58 - Math.Abs(x - (45 + phase * 32)));
                var horizon = y > 70 ? 28 : 0;
                var offset = y * stride + x * 4;

                ptr[offset + 0] = (byte)Math.Clamp(18 + accentB / 3 + highlight / 3 + horizon, 0, 255);
                ptr[offset + 1] = (byte)Math.Clamp(24 + accentG / 3 + glow + highlight / 4, 0, 255);
                ptr[offset + 2] = (byte)Math.Clamp(32 + accentR / 2 + highlight + phase * 12, 0, 255);
                ptr[offset + 3] = 255;
            }
        }

        return bitmap;
    }

    private static RenderTaskViewModel CreateTask(
        string fileName,
        string sceneName,
        int startFrame,
        int endFrame,
        RenderTaskStatus status,
        int completedFrames,
        double progress01,
        double frameProgress01)
    {
        var task = new DesignTimeRenderTaskViewModel
        {
            BlendFilePath = $"/Users/atticus/Desktop/RenderQueue/{fileName}",
            StartFrame = startFrame,
            EndFrame = endFrame,
            Animation = true,
            OverrideScene = true,
            SelectedSceneName = sceneName,
            IsValid = true,
            Enable = true,
            Status = status,
            CurrentFrame = Math.Max(startFrame, startFrame + completedFrames),
            CompletedFrames = completedFrames,
            OverallProgress01 = progress01,
            Progress01 = frameProgress01,
            Engine = "Eevee",
            AvailableSceneNames = ["Scene", "Camera", "Main"]
        };

        task.FileInfo = new BlendFileInfo
        {
            FilePath = task.BlendFilePath,
            FileSizeBytes = 79062630,
            CreatedTime = new DateTime(2026, 6, 4, 19, 49, 8),
            LastModifiedTime = new DateTime(2026, 6, 4, 19, 49, 8)
        };

        return task;
    }

    public void SetGlobalRenderTimeout(int timeoutSeconds) { }
    public void SetGlobalMaxRetryAttempts(int maxRetryAttempts) { }
    public void SetVideoCodec(string codec) { }
    public void SetVideoQuality(string quality) { }
    public void SetBlenderPath(string blenderPath) { }
    public bool IsBlenderServiceReady() => true;
    public void AddBlendFiles(IEnumerable<string> filePaths)
    {
        var paths = filePaths as string[] ?? filePaths.ToArray();
        if (paths.Length == 0)
        {
            paths = [$"Studio_Shot_{_addedTaskIndex:00}.blend"];
        }

        foreach (var path in paths)
        {
            var fileName = System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"Studio_Shot_{_addedTaskIndex:00}.blend";
            }

            RenderTasks.Add(CreateTask(fileName, "Camera", 1, 120 + _addedTaskIndex * 24, RenderTaskStatus.Pending, 0, 0, 0));
            _addedTaskIndex++;
        }

        if (_queueState == QueueExecutionState.Completed)
        {
            _queueState = QueueExecutionState.Idle;
        }

        PublishSnapshot();
    }

    public void AddDroppedFiles(IEnumerable<string> filePaths) => AddBlendFiles(filePaths);

    public void RemoveSelectedTask(RenderTaskViewModel? selectedTask, Action<RenderTaskViewModel?> setSelectedTask)
    {
        RemoveTask(selectedTask, selectedTask, setSelectedTask);
    }

    public void RemoveTask(RenderTaskViewModel? taskToRemove, RenderTaskViewModel? selectedTask, Action<RenderTaskViewModel?> setSelectedTask)
    {
        if (taskToRemove == null)
        {
            return;
        }

        var index = RenderTasks.IndexOf(taskToRemove);
        if (index < 0)
        {
            return;
        }

        if (CurrentRenderingTask == taskToRemove)
        {
            CurrentRenderingTask = null;
            _demoTimer.Stop();
            _queueState = QueueExecutionState.Idle;
        }

        RenderTasks.RemoveAt(index);
        setSelectedTask(RenderTasks.ElementAtOrDefault(Math.Min(index, RenderTasks.Count - 1)));
        PublishSnapshot();
    }

    public void RemoveAllTasks()
    {
        _demoTimer.Stop();
        CurrentRenderingTask = null;
        RenderTasks.Clear();
        _queueState = QueueExecutionState.Idle;
        PublishSnapshot();
    }

    public void RemoveCompletedTasks()
    {
        foreach (var task in RenderTasks.Where(task => task.Status == RenderTaskStatus.Completed).ToArray())
        {
            RenderTasks.Remove(task);
        }

        PublishSnapshot();
    }

    public Task StartQueueAsync()
    {
        if (_queueState is QueueExecutionState.Idle or QueueExecutionState.Completed)
        {
            StartNextEnabledTask(resetProgress: true);
        }

        return Task.CompletedTask;
    }

    public void StopQueue()
    {
        _demoTimer.Stop();
        if (CurrentRenderingTask != null)
        {
            CurrentRenderingTask.Status = RenderTaskStatus.Cancelled;
        }

        CurrentRenderingTask = null;
        _queueState = QueueExecutionState.Idle;
        PublishSnapshot();
    }

    public void PauseQueue()
    {
        if (_queueState != QueueExecutionState.Running || CurrentRenderingTask == null)
        {
            return;
        }

        _demoTimer.Stop();
        CurrentRenderingTask.Status = RenderTaskStatus.Paused;
        _queueState = QueueExecutionState.Paused;
        PublishSnapshot();
    }

    public Task ResumeQueueAsync()
    {
        if (_queueState == QueueExecutionState.Paused && CurrentRenderingTask != null)
        {
            CurrentRenderingTask.Status = RenderTaskStatus.Running;
            _queueState = QueueExecutionState.Running;
            _demoTimer.Start();
            PublishSnapshot();
        }

        return Task.CompletedTask;
    }

    public void MoveTaskUp(RenderTaskViewModel? selectedTask) => MoveTask(selectedTask, -1);
    public void MoveTaskDown(RenderTaskViewModel? selectedTask) => MoveTask(selectedTask, 1);
    public void MoveTaskToTop(RenderTaskViewModel? selectedTask) => MoveTaskTo(selectedTask, 0);
    public void MoveTaskToBottom(RenderTaskViewModel? selectedTask) => MoveTaskTo(selectedTask, RenderTasks.Count - 1);

    public void CopyTask(RenderTaskViewModel? taskToCopy, Action<RenderTaskViewModel?> setSelectedTask)
    {
        if (taskToCopy == null)
        {
            return;
        }

        var copy = CreateTask(
            $"{System.IO.Path.GetFileNameWithoutExtension(taskToCopy.BlendFileName)}_Copy.blend",
            taskToCopy.SelectedSceneName,
            taskToCopy.RealStartFrame,
            taskToCopy.RealEndFrame,
            RenderTaskStatus.Pending,
            0,
            0,
            0);

        RenderTasks.Add(copy);
        setSelectedTask(copy);
        PublishSnapshot();
    }

    public void RequestRemoveAllTasksConfirmation() => RemoveAllTasks();
    public Task LoadQueueDataAsync() => Task.CompletedTask;

    private void MoveTask(RenderTaskViewModel? selectedTask, int offset)
    {
        if (selectedTask == null)
        {
            return;
        }

        MoveTaskTo(selectedTask, RenderTasks.IndexOf(selectedTask) + offset);
    }

    private void MoveTaskTo(RenderTaskViewModel? selectedTask, int newIndex)
    {
        if (selectedTask == null)
        {
            return;
        }

        var oldIndex = RenderTasks.IndexOf(selectedTask);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= RenderTasks.Count || oldIndex == newIndex)
        {
            return;
        }

        RenderTasks.Move(oldIndex, newIndex);
        PublishSnapshot();
    }

    public void Dispose()
    {
        _demoTimer.Stop();
        _demoTimer.Tick -= OnDemoTimerTick;
        foreach (var frame in _mockFrames)
        {
            frame.Dispose();
        }
    }
}

internal sealed class PreviewRenderLogService : IRenderLogService
{
    public string CurrentSessionId => "browser-preview";
    public event EventHandler<RenderLogEvent>? LogAppended;
    public IReadOnlyList<RenderLogEvent> GetEvents(RenderLogProjection? projection = null) => [];
    public void Write(RenderLogEvent logEvent) => LogAppended?.Invoke(this, logEvent);
    public void Write(
        RenderLogLevel level,
        RenderLogScope scope,
        string message,
        Guid? taskId = null,
        string? blendFilePath = null,
        string? source = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
    }

    public void ClearHistory() { }
    public void ClearAll() { }
}

internal sealed class PreviewSettingsPersistenceService : ISettingsPersistenceService
{
    public Task<bool> SaveSettingsAsync(SettingsData settings) => Task.FromResult(true);

    public Task<SettingsData> LoadSettingsAsync()
    {
        var blender = new BlenderExecutable
        {
            Path = "/Applications/Blender.app/Contents/MacOS/Blender",
            Version = "4.3.2",
            Branch = "main",
            Hash = "b4a4f1c8742f",
            Platform = "macOS arm64",
            Type = "Release",
            BuildDate = new DateTime(2026, 5, 28),
            BuildTime = "14:22:08",
            CommitDate = new DateTime(2026, 5, 27),
            CommitTime = "21:48:11",
            IsValid = true,
            LastValidated = new DateTime(2026, 6, 4, 19, 49, 8)
        };

        return Task.FromResult(new SettingsData
        {
            BlenderExecutables = [blender],
            SelectedBlenderPath = blender.Path,
            DefaultRenderTimeoutSeconds = 600,
            MaxRetryAttempts = 3,
            VideoCodec = "H264",
            VideoQuality = "PERC_LOSSLESS",
            Language = "en-US",
            BaseTheme = "Dark",
            UseGpu = true
        });
    }
}

internal sealed class PreviewBlenderValidationService : IBlenderValidationService
{
    private int _requestVersion;

    public BlenderValidationRequest BeginValidation(string? path, string channel = BlenderValidationService.DefaultChannel)
    {
        return new BlenderValidationRequest(path ?? string.Empty, channel, Interlocked.Increment(ref _requestVersion), CancellationToken.None);
    }

    public BlenderValidationResult? ValidatePreconditions(BlenderValidationRequest request) => null;

    public Task<BlenderValidationResult> ValidateAsync(BlenderValidationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateSuccess(request.Path, request.RequestVersion));
    }

    public Task<BlenderValidationResult> ValidatePathAsync(string? path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateSuccess(path ?? string.Empty, _requestVersion));
    }

    public bool IsCurrent(BlenderValidationRequest request) => request.RequestVersion == _requestVersion;
    public void CancelCurrent(string channel = BlenderValidationService.DefaultChannel) => Interlocked.Increment(ref _requestVersion);

    private static BlenderValidationResult CreateSuccess(string path, int requestVersion)
    {
        return new BlenderValidationResult
        {
            Status = BlenderValidationStatus.Success,
            Path = path,
            RequestVersion = requestVersion,
            IsCurrent = true,
            VersionInfo = new BlenderVersionInfo
            {
                Product = "Blender",
                Version = path.Contains("4.4", StringComparison.Ordinal) ? "4.4.0 Alpha" : "4.3.2",
                Branch = path.Contains("4.4", StringComparison.Ordinal) ? "experimental" : "main",
                Hash = path.Contains("4.4", StringComparison.Ordinal) ? "e81c2da94710" : "b4a4f1c8742f",
                Platform = "macOS arm64",
                Type = path.Contains("4.4", StringComparison.Ordinal) ? "Alpha" : "Release",
                BuildDate = path.Contains("4.4", StringComparison.Ordinal) ? new DateTime(2026, 6, 2) : new DateTime(2026, 5, 28),
                BuildTime = path.Contains("4.4", StringComparison.Ordinal) ? "09:17:31" : "14:22:08",
                CommitDate = path.Contains("4.4", StringComparison.Ordinal) ? new DateTime(2026, 6, 1) : new DateTime(2026, 5, 27),
                CommitTime = path.Contains("4.4", StringComparison.Ordinal) ? "22:04:56" : "21:48:11"
            }
        };
    }
}
