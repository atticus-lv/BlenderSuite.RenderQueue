using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderSuite.RenderQueue.Models;
using Avalonia.Media.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using BlenderSuite.RenderQueue.Extensions;
using BlenderSuite.RenderQueue.Views;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.Services.Business.Blender.BlenderProcess;
using BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.UI;
using BlenderSuite.RenderQueue.Helpers;
using BlenderSuite.RenderQueue.Localizer;
using BlenderSuite.RenderQueue.ViewModels.Logs;

namespace BlenderSuite.RenderQueue.ViewModels;

public partial class RenderTaskViewModel : ViewModelBase
{
    private static int s_sharedDetailTabIndex;
    private static int s_sharedLogTabIndex;

    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _blendFilePath = string.Empty;

    [ObservableProperty]
    private int _startFrame = 1;

    [ObservableProperty]
    private int _endFrame = 1;

    [ObservableProperty]
    private bool _animation = true;

    [ObservableProperty]
    private bool _overrideFrameRange;

    [ObservableProperty]
    private bool _overrideScene;

    [ObservableProperty]
    private string _selectedSceneName = string.Empty;

    [ObservableProperty]
    private bool _autoStart = true;

    [ObservableProperty]
    private bool _enable = true;

    [ObservableProperty]
    private bool _isValid = true;

    [ObservableProperty]
    private List<string> _availableSceneNames = [];

    public bool HasValidSceneSelection =>
        !string.IsNullOrEmpty(SelectedSceneName) && ScenePropertiesView.SceneNames.Contains(SelectedSceneName);

    public bool ShowSceneOverrideWarning => OverrideScene && !HasValidSceneSelection;


    public bool IsOverrideSceneIsDefaultScene => OverrideScene &&
                                                 !string.IsNullOrEmpty(SelectedSceneName) &&
                                                 SelectedSceneName == ScenePropertiesView.SelectedSceneName;

    partial void OnEnableChanged(bool value)
    {
        EnableChanged?.Invoke(this, EventArgs.Empty);
        UpdateStatusDependentProperties();
    }

    partial void OnStatusChanged(RenderTaskStatus value)
    {
        UpdateStatusDependentProperties();
    }

    partial void OnIsValidChanged(bool value)
    {
        UpdateStatusDependentProperties();
    }

    partial void OnIsGeneratingVideoChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerateVideo));
    }

    [ObservableProperty]
    private bool _isDropTarget;

    [ObservableProperty]
    private bool _isDragTarget;

    [ObservableProperty]
    private bool _isPendingDeletion;

    private bool _isQueueRunning;


    [ObservableProperty]
    private double _progress01; // The current frame progress

    [ObservableProperty]
    private double _overallProgress01; // Overall progress

    [ObservableProperty]
    private string _engine = string.Empty;

    [ObservableProperty]
    private string _statusDetailText = string.Empty;

    [ObservableProperty]
    private int _currentFrame;

    [ObservableProperty]
    private int _completedFrames;

    public int TotalFrames => Math.Max(0, EndFrame - StartFrame + 1);

    public int DisplayStartFrame => OverrideFrameRange
        ? StartFrame
        : (ScenePropertiesView.SceneProperties.IsLoaded ? ScenePropertiesView.SceneProperties.FrameStart : StartFrame);

    public int DisplayEndFrame => OverrideFrameRange
        ? EndFrame
        : (ScenePropertiesView.SceneProperties.IsLoaded ? ScenePropertiesView.SceneProperties.FrameEnd : EndFrame);

    public int DisplayTotalFrames => Math.Max(0, DisplayEndFrame - DisplayStartFrame + 1);

    // 实际渲染用的帧范围属性（优先级：覆写帧范围 > 覆写场景帧范围 > 默认场景帧范围）
    public int RealStartFrame
    {
        get
        {
            if (OverrideFrameRange)
            {
                return StartFrame;
            }

            if (OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) &&
                ScenePropertiesView.AllScenes.TryGetValue(SelectedSceneName, out var value))
            {
                return value.FrameStart;
            }

            // If the scene properties are loaded, use the frame range of the scene
            return ScenePropertiesView.SceneProperties.IsLoaded
                ? ScenePropertiesView.SceneProperties.FrameStart
                : StartFrame;
        }
    }

    public int RealEndFrame
    {
        get
        {
            if (OverrideFrameRange)
            {
                return EndFrame;
            }

            if (OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) &&
                ScenePropertiesView.AllScenes.TryGetValue(SelectedSceneName, out var value))
            {
                return value.FrameEnd;
            }

            return ScenePropertiesView.SceneProperties.IsLoaded
                ? ScenePropertiesView.SceneProperties.FrameEnd
                :
                // If the scene properties are not loaded, but there is an override setting, use the overridden frame range as a fallback
                EndFrame;
        }
    }

    public int RealTotalFrames => Math.Max(0, RealEndFrame - RealStartFrame + 1);

    // Attributes related to task operation permissions

    public bool CanModifyEnable =>
        IsValid && Status is RenderTaskStatus.Pending or RenderTaskStatus.Completed or RenderTaskStatus.Cancelled
            or RenderTaskStatus.Failed;


    public bool CanModifyOverride =>
        IsValid && Status is RenderTaskStatus.Pending or RenderTaskStatus.Completed or RenderTaskStatus.Cancelled
            or RenderTaskStatus.Failed;

    public bool CanDelete => IsValid || Status == RenderTaskStatus.Pending;

    public bool CanRefresh => Status != RenderTaskStatus.Running && !_isQueueRunning;

    public bool ShowProgress => IsValid && Status is RenderTaskStatus.Running or RenderTaskStatus.Paused;


    public bool CanGenerateVideo => IsValid && !IsGeneratingVideo &&
                                    !string.IsNullOrEmpty(FinalSceneProperties.FramePath) && _processService != null;


    public string StatusText => Status.GetLocalizationKey();

    public bool HasStatusDetailText => !string.IsNullOrWhiteSpace(StatusDetailText);

    partial void OnStatusDetailTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusDetailText));
    }


    private void UpdateStatusDependentProperties()
    {
        OnPropertyChanged(nameof(CanModifyEnable));
        OnPropertyChanged(nameof(CanModifyOverride));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(CanGenerateVideo));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatusDetailText));
    }


    public void SetGlobalRenderTimeout(int timeoutSeconds)
    {
        _globalRenderTimeoutSeconds = timeoutSeconds;
    }

    internal int GetGlobalRenderTimeoutSeconds()
    {
        return _globalRenderTimeoutSeconds;
    }

    public void SetGlobalMaxRetryAttempts(int maxRetryAttempts)
    {
        _globalMaxRetryAttempts = maxRetryAttempts;
    }

    internal int GetGlobalMaxRetryAttempts()
    {
        return _globalMaxRetryAttempts;
    }


    public void SetQueueRunningState(bool isQueueRunning)
    {
        if (_isQueueRunning == isQueueRunning) return;
        _isQueueRunning = isQueueRunning;
        OnPropertyChanged(nameof(CanRefresh));
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Queue running state changed to: {isQueueRunning}, CanRefresh: {CanRefresh}", Id, BlendFilePath, "RenderTaskViewModel");
    }


    public void SetVideoCodec(string codec)
    {
        _videoCodec = codec;
    }


    public void SetVideoQuality(string quality)
    {
        _videoQuality = quality;
    }


    public void SetProcessService(BlenderProcessService? processService)
    {
        _processService = processService;
        OnPropertyChanged(nameof(CanGenerateVideo));
    }

    partial void OnStartFrameChanged(int value)
    {
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(DisplayStartFrame));
        OnPropertyChanged(nameof(DisplayTotalFrames));
        OnPropertyChanged(nameof(RealStartFrame));
        OnPropertyChanged(nameof(RealTotalFrames));

        FrameRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnEndFrameChanged(int value)
    {
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(DisplayEndFrame));
        OnPropertyChanged(nameof(DisplayTotalFrames));
        OnPropertyChanged(nameof(RealEndFrame));
        OnPropertyChanged(nameof(RealTotalFrames));

        FrameRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnOverrideFrameRangeChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayStartFrame));
        OnPropertyChanged(nameof(DisplayEndFrame));
        OnPropertyChanged(nameof(DisplayTotalFrames));
        OnPropertyChanged(nameof(RealStartFrame));
        OnPropertyChanged(nameof(RealEndFrame));
        OnPropertyChanged(nameof(RealTotalFrames));

        OverrideFrameRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnOverrideSceneChanged(bool value)
    {
        // 触发相关属性更新
        OnPropertyChanged(nameof(HasValidSceneSelection));
        OnPropertyChanged(nameof(ShowSceneOverrideWarning));
        OnPropertyChanged(nameof(IsOverrideSceneIsDefaultScene));
        OnPropertyChanged(nameof(RealStartFrame));
        OnPropertyChanged(nameof(RealEndFrame));
        OnPropertyChanged(nameof(RealTotalFrames));
        OnPropertyChanged(nameof(FinalSceneProperties));
        OnPropertyChanged(nameof(FramePathDirectory));

        OverrideSceneChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedSceneNameChanged(string value)
    {
        OnPropertyChanged(nameof(HasValidSceneSelection));
        OnPropertyChanged(nameof(ShowSceneOverrideWarning));
        OnPropertyChanged(nameof(IsOverrideSceneIsDefaultScene));
        OnPropertyChanged(nameof(RealStartFrame));
        OnPropertyChanged(nameof(RealEndFrame));
        OnPropertyChanged(nameof(RealTotalFrames));
        OnPropertyChanged(nameof(FinalSceneProperties));
        OnPropertyChanged(nameof(FramePathDirectory));

        SceneSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    private string _sampleText = string.Empty;

    [ObservableProperty]
    private string _savedPath = string.Empty;

    [ObservableProperty]
    private string _outputLog = string.Empty;

    [ObservableProperty]
    private int _selectedDetailTabIndex;

    [ObservableProperty]
    private int _selectedLogTabIndex;

    [ObservableProperty]
    private ObservableCollection<TaskLogEntryViewModel> _timelineEntries = [];

    [ObservableProperty]
    private ObservableCollection<TaskLogEntryViewModel> _debugEntries = [];

    [ObservableProperty]
    private string _debugLogText = string.Empty;

    [ObservableProperty]
    private bool _isOutputPreviewTabRealized;

    [ObservableProperty]
    private bool _isLogTabRealized;

    [ObservableProperty]
    private bool _isRenderSettingsTabRealized;

    [ObservableProperty]
    private BlendScenePropertiesViewModel _scenePropertiesView = null!;

    public RenderTaskViewModel? RenderSettingsContent => IsRenderSettingsTabRealized ? this : null;
    public RenderTaskViewModel? OutputPreviewContent => IsOutputPreviewTabRealized ? this : null;
    public RenderTaskViewModel? LogContent => IsLogTabRealized ? this : null;

    partial void OnSelectedDetailTabIndexChanged(int value)
    {
        s_sharedDetailTabIndex = value;
        SelectionPerfTrace.Mark(Id, BlendFileName, "RenderTaskView.TabChanged", $"index={value}");

        if (value == 2 && !IsOutputPreviewTabRealized)
        {
            IsOutputPreviewTabRealized = true;
            OnPropertyChanged(nameof(OutputPreviewContent));
            SelectionPerfTrace.Mark(Id, BlendFileName, "RenderTaskView.OutputPreviewRealized");
        }

        if (value == 3 && !IsLogTabRealized)
        {
            IsLogTabRealized = true;
            OnPropertyChanged(nameof(LogContent));
            SelectionPerfTrace.Mark(Id, BlendFileName, "RenderTaskView.LogRealized");
        }

        if (value == 1 && !IsRenderSettingsTabRealized)
        {
            IsRenderSettingsTabRealized = true;
            OnPropertyChanged(nameof(RenderSettingsContent));
            SelectionPerfTrace.Mark(Id, BlendFileName, "RenderTaskView.RenderSettingsRealized");
        }
    }

    partial void OnSelectedLogTabIndexChanged(int value)
    {
        s_sharedLogTabIndex = value;
        SelectionPerfTrace.Mark(Id, BlendFileName, "RenderTaskView.LogTabChanged", $"index={value}");
    }

    partial void OnScenePropertiesViewChanged(BlendScenePropertiesViewModel value)
    {
        OnPropertyChanged(nameof(FinalSceneProperties));
        OnPropertyChanged(nameof(FramePathDirectory));

        if (value == null) return;
        value.PropertyChanged += (sender, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(value.SelectedSceneProperties) or nameof(value.SceneProperties) or nameof(value.AllScenes):
                    OnPropertyChanged(nameof(FinalSceneProperties));
                    OnPropertyChanged(nameof(FramePathDirectory));
                    break;
                
                case nameof(value.IsLoading):
                {
                    if (!value.IsLoading)
                    {
                        OnPropertyChanged(nameof(DisplayStartFrame));
                        OnPropertyChanged(nameof(DisplayEndFrame));
                        OnPropertyChanged(nameof(DisplayTotalFrames));
                        OnPropertyChanged(nameof(RealStartFrame));
                        OnPropertyChanged(nameof(RealEndFrame));
                        OnPropertyChanged(nameof(RealTotalFrames));
                        OnPropertyChanged(nameof(FinalSceneProperties));
                        OnPropertyChanged(nameof(FramePathDirectory));
                    }

                    break;
                }
            }
        };
    }

    [ObservableProperty]
    private BlendFileInfo _fileInfo = new();

    [ObservableProperty]
    private Bitmap? _renderedImage;

    [ObservableProperty]
    private string _renderedImagePath = string.Empty;

    [ObservableProperty]
    private bool _hasRenderedImage = false;

    [RelayCommand]
    private void OpenImagePreview()
    {
        if (!HasRenderedImage || string.IsNullOrEmpty(RenderedImagePath))
        {
            return;
        }

        try
        {
            var viewModel = new ImagePreviewWindowViewModel(RenderedImagePath, CurrentFrame);
            var window = new ImagePreviewWindow(viewModel);

            // 显示窗口
            window.ShowWindow();

            _logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"✅ Image preview window opened: {RenderedImagePath}", Id, BlendFilePath, "RenderTaskViewModel");
        }
        catch (Exception ex)
        {
            _logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Error opening image preview: {ex.Message}", Id, BlendFilePath, "RenderTaskViewModel");
        }
    }

    [RelayCommand]
    private void RefreshFileInfo()
    {
        try
        {
            EnqueueLog("[INFO] Requesting task refresh...");

            // 触发事件，请求父级刷新任务
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            EnqueueLog($"[ERROR] Failed to request refresh: {ex.Message}");
        }
    }

    public async Task RefreshFilePropertiesAsync(string blenderPath)
    {
        await _filePropertiesCoordinator.RefreshAsync(blenderPath);
    }


    public event EventHandler? RefreshRequested;


    public event EventHandler? EnableChanged;


    public event EventHandler<OpenInBlenderRequestedEventArgs>? OpenInBlenderRequested;


    public event EventHandler<OpenSysDirectoryRequestedEventArgs>? OpenFileDirectoryRequested;


    public event EventHandler? OverrideFrameRangeChanged;


    public event EventHandler? OverrideSceneChanged;


    public event EventHandler? SceneSelectionChanged;


    public event EventHandler? FrameRangeChanged;


    public string BlendFileName => Path.GetFileName(BlendFilePath);

    [ObservableProperty]
    private bool _isLogPaused;

    [ObservableProperty]
    private string _logPauseButtonText = "Stop Log";

    // 视频生成相关属性
    [ObservableProperty]
    private bool _isGeneratingVideo; // 是否正在生成视频

    [ObservableProperty]
    private double _videoGenerationProgress; // 视频生成进度

    [ObservableProperty]
    private string _videoGenerationStatus = string.Empty; // 视频生成状态

    // 全局超时设置（从SettingsViewModel获取）
    private int _globalRenderTimeoutSeconds = 300; // 默认5分钟
    private int _globalMaxRetryAttempts = 3; // 默认最大重试3次

    // 视频生成相关设置
    private string _videoCodec = "H264"; // 默认使用H264编码
    private string _videoQuality = "PERC_LOSSLESS"; // 默认感知无损质量
    private BlenderProcessService? _processService; // 进程管理服务

    [ObservableProperty]
    private RenderTaskStatus _status = RenderTaskStatus.Pending;

    [ObservableProperty]
    private DateTime? _startTime;

    [ObservableProperty]
    private DateTime? _endTime;

    [ObservableProperty]
    private TimeSpan? _duration;

    public BlendSceneProperties FinalSceneProperties
    {
        get
        {
            if (OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) &&
                ScenePropertiesView.AllScenes.TryGetValue(SelectedSceneName, out var value))
            {
                return value;
            }

            return ScenePropertiesView.SelectedSceneProperties;
        }
    }


    public string? FramePathDirectory
    {
        get
        {
            var framePath = FinalSceneProperties.FramePath;
            return !string.IsNullOrEmpty(framePath) ? Path.GetDirectoryName(framePath)?.Replace("\\", "/") : null;
        }
    }
    // 内部状态
    private IRenderLogService? _logService;
    private DateTimeOffset? _logClearCutoff;
    private readonly RenderTaskFilePropertiesCoordinator _filePropertiesCoordinator;
    private readonly RenderTaskPreviewService _previewService;
    private readonly RenderTaskLogProjection _logProjection;
    private readonly RenderTaskVideoGenerationCoordinator _videoGenerationCoordinator;

    // 事件
    public event EventHandler<RenderTaskStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<RenderTaskProgressEventArgs>? ProgressChanged;

    public RenderTaskViewModel(BlendScenePropertiesViewModel scenePropertiesView)
    {
        ScenePropertiesView = scenePropertiesView;
        _filePropertiesCoordinator = new RenderTaskFilePropertiesCoordinator(this);
        _previewService = new RenderTaskPreviewService(this);
        _logProjection = new RenderTaskLogProjection(this);
        _videoGenerationCoordinator = new RenderTaskVideoGenerationCoordinator(this);
        _selectedDetailTabIndex = s_sharedDetailTabIndex;
        _selectedLogTabIndex = s_sharedLogTabIndex;

        // 手动触发ScenePropertiesView的初始化
        OnScenePropertiesViewChanged(ScenePropertiesView);
    }

    internal void SyncSharedTabSelection()
    {
        if (SelectedDetailTabIndex != s_sharedDetailTabIndex)
        {
            SelectedDetailTabIndex = s_sharedDetailTabIndex;
        }

        if (SelectedLogTabIndex != s_sharedLogTabIndex)
        {
            SelectedLogTabIndex = s_sharedLogTabIndex;
        }
    }

    public RenderTaskViewModel(
        BlendScenePropertiesViewModel scenePropertiesView,
        string blendFilePath,
        int startFrame,
        int endFrame,
        bool animation = true,
        bool overrideFrameRange = false) : this(scenePropertiesView)
    {
        BlendFilePath = blendFilePath;
        StartFrame = startFrame;
        EndFrame = endFrame;
        Animation = animation;
        OverrideFrameRange = overrideFrameRange;

        // 检查文件有效性
        IsValid = !string.IsNullOrEmpty(blendFilePath) && File.Exists(blendFilePath);

        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Constructor - ID: {Id}, File: {Path.GetFileName(blendFilePath)}, IsValid: {IsValid}", Id, BlendFilePath, "RenderTaskViewModel");
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Initial ScenePropertiesView state - IsLoading: {ScenePropertiesView.IsLoading}, IsLoaded: {ScenePropertiesView.SceneProperties.IsLoaded}, ShowEmptyState: {ScenePropertiesView.ShowEmptyState}", Id, BlendFilePath, "RenderTaskViewModel");

        // 加载文件信息
        LoadFileInfo();
    }

    /// <summary>
    /// 从 RenderTaskInfo 数据创建 RenderTaskViewModel 实例
    /// 如果 RenderTaskInfo 中有 UUID 则使用，否则生成新的
    /// </summary>
    public RenderTaskViewModel(BlendScenePropertiesViewModel scenePropertiesView, RenderTaskInfo taskInfo) : this(scenePropertiesView)
    {
        // 使用保存的 UUID，如果为空则生成新的
        Id = taskInfo.Id == Guid.Empty ? Guid.NewGuid() : taskInfo.Id;

        BlendFilePath = taskInfo.Filepath;
        StartFrame = taskInfo.StartFrame;
        EndFrame = taskInfo.EndFrame;
        Enable = taskInfo.Enable;
        Animation = StartFrame != EndFrame;

        // 处理覆写数据
        if (taskInfo.Override != null)
        {
            if (taskInfo.Override.OverrideFrameRange != null)
            {
                OverrideFrameRange = true;
                StartFrame = taskInfo.Override.OverrideFrameRange.StartFrame;
                EndFrame = taskInfo.Override.OverrideFrameRange.EndFrame;
                Animation = StartFrame != EndFrame;
            }

            if (taskInfo.Override.OverrideScene != null)
            {
                OverrideScene = true;
                SelectedSceneName = taskInfo.Override.OverrideScene.SceneName;
            }
        }

        // 检查文件有效性
        IsValid = !string.IsNullOrEmpty(BlendFilePath) && File.Exists(BlendFilePath);

        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Constructor from RenderTaskInfo - ID: {Id}, File: {Path.GetFileName(BlendFilePath)}, IsValid: {IsValid}", Id, BlendFilePath, "RenderTaskViewModel");

        // 加载文件信息
        LoadFileInfo();
    }

    private void LoadFileInfo()
    {
        if (!string.IsNullOrEmpty(BlendFilePath))
        {
            FileInfo = BlendFileInfo.FromFilePath(BlendFilePath);
        }
    }


    /// <summary>
    /// 加载并优化渲染图片
    /// </summary>
    private async Task LoadRenderedImageAsync(string imagePath)
    {
        await _previewService.LoadRenderedImageAsync(imagePath);
    }

    /// <summary>
    /// 从文件加载并优化图片尺寸
    /// </summary>
    private async Task<Bitmap?> LoadAndOptimizeImageAsync(string imagePath, int maxWidth, int maxHeight)
    {
        return await _previewService.LoadAndOptimizeImageAsync(imagePath, maxWidth, maxHeight);
    }

    public async Task LoadFilePropertiesAsync(string blenderPath)
    {
        await _filePropertiesCoordinator.LoadAsync(blenderPath);
    }

    internal void NotifyMissingBlendFile()
    {
        EnqueueLog("请先选择 .blend 文件");
    }

    internal void BeginRenderExecution(bool isResume, bool resetRetryBudget)
    {
        if (resetRetryBudget)
        {
            StatusDetailText = string.Empty;
        }

        SetStatus(RenderTaskStatus.Running);
        if (!isResume || !StartTime.HasValue)
        {
            StartTime = DateTime.Now;
        }
    }

    internal void FinalizeStopped()
    {
        SetStatus(RenderTaskStatus.Cancelled);
        EndTime = DateTime.Now;
        if (StartTime.HasValue)
        {
            Duration = EndTime.Value - StartTime.Value;
        }

        EnqueueLog("渲染已停止");
    }

    internal void FinalizePaused()
    {
        SetStatus(RenderTaskStatus.Paused);
        EnqueueLog($"渲染已暂停，当前帧: {CurrentFrame}");
    }

    internal void FinalizeCancelled(string logMessage)
    {
        EnqueueLog(logMessage);
        SetStatus(RenderTaskStatus.Cancelled);
    }

    internal void FinalizeFailed(string detail, string logMessage)
    {
        EnqueueLog(logMessage);
        StatusDetailText = detail;
        SetStatus(RenderTaskStatus.Failed);
    }

    internal void FinalizeCompleted()
    {
        OverallProgress01 = 1;
        StatusDetailText = string.Empty;
        SetStatus(RenderTaskStatus.Completed);
        EndTime = DateTime.Now;
        if (StartTime.HasValue)
        {
            Duration = EndTime.Value - StartTime.Value;
        }
    }

    internal void SetStatusDetail(string detail)
    {
        StatusDetailText = detail;
    }

    internal void LogLine(string line)
    {
        EnqueueLog(line);
    }

    internal static string FormatLocalized(string key, params object[] args)
    {
        var format = Localizer.Localizer.Instance[key];
        return args.Length == 0 ? format : string.Format(format, args);
    }

    internal BlenderWorkerRequest BuildWorkerRequest(int? resumeFromFrame = null)
    {
        var sceneName = OverrideScene && !string.IsNullOrWhiteSpace(SelectedSceneName) ? SelectedSceneName : null;
        var startFrame = resumeFromFrame ?? RealStartFrame;
        var endFrame = RealEndFrame;

        if (Animation && startFrame != endFrame)
        {
            return new BlenderWorkerRequest
            {
                BlendFilePath = BlendFilePath,
                Animation = true,
                FrameStart = startFrame,
                FrameEnd = endFrame,
                SceneName = sceneName
            };
        }

        return new BlenderWorkerRequest
        {
            BlendFilePath = BlendFilePath,
            Animation = false,
            SingleFrame = startFrame,
            SceneName = sceneName
        };
    }

    internal string DescribeWorkerRequest(BlenderWorkerRequest request)
    {
        var sceneText = string.IsNullOrWhiteSpace(request.SceneName) ? "默认场景" : request.SceneName;
        return request.Animation
            ? $"动画 {request.FrameStart}..{request.FrameEnd}, 场景={sceneText}"
            : $"单帧 {request.SingleFrame}, 场景={sceneText}";
    }

    internal int GetResumeFrameForRetry(BlenderWorkerRequest request)
    {
        if (Animation && RealStartFrame != RealEndFrame)
        {
            var resumeFrame = CurrentFrame > 0 ? CurrentFrame : request.FrameStart ?? RealStartFrame;
            return Math.Clamp(resumeFrame, RealStartFrame, RealEndFrame);
        }

        return request.SingleFrame ?? RealStartFrame;
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logProjection.Clear();
    }

    [RelayCommand]
    private void ToggleLogPause()
    {
        IsLogPaused = !IsLogPaused;
        LogPauseButtonText = IsLogPaused ? "Resume Log" : "Stop Log";
    }

    [RelayCommand]
    private async Task CopyDebugDiagnostics()
    {
        if (string.IsNullOrWhiteSpace(DebugLogText))
        {
            return;
        }

        await ClipboardHelper.SetText(DebugLogText, this);
    }

    [RelayCommand]
    private void OpenInBlender()
    {
        try
        {
            if (string.IsNullOrEmpty(BlendFilePath) || !File.Exists(BlendFilePath))
            {
                EnqueueLog("[ERROR] 文件不存在，无法在Blender中打开");
                return;
            }

            // 触发事件，请求父级提供Blender路径并打开文件
            OpenInBlenderRequested?.Invoke(this, new OpenInBlenderRequestedEventArgs(BlendFilePath));
        }
        catch (Exception ex)
        {
            EnqueueLog($"[ERROR] 打开Blender失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFileDirectory()
    {
        try
        {
            if (string.IsNullOrEmpty(BlendFilePath))
            {
                EnqueueLog("[ERROR] 文件路径为空，无法打开所在文件夹");
                return;
            }

            OpenFileDirectoryRequested?.Invoke(this, new OpenSysDirectoryRequestedEventArgs(BlendFilePath));
        }
        catch (Exception ex)
        {
            EnqueueLog($"[ERROR] 打开文件夹失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFramePathDirectory()
    {
        try
        {
            if (string.IsNullOrEmpty(FramePathDirectory))
            {
                EnqueueLog("[ERROR] 文件路径为空，无法打开所在文件夹");
                return;
            }

            // 触发事件，请求父级打开文件所在文件夹
            OpenFileDirectoryRequested?.Invoke(this, new OpenSysDirectoryRequestedEventArgs(FramePathDirectory));
        }
        catch (Exception ex)
        {
            EnqueueLog($"[ERROR] 打开文件夹失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task GenerateVideo()
    {
        await _videoGenerationCoordinator.GenerateVideoAsync();
    }

    internal void HandleRawOutputLine(string line, IRenderOutputParser parser)
    {
        try
        {
            var events = parser.ParseLine(line);
            foreach (var parsedEvent in events)
            {
                switch (parsedEvent)
                {
                    case RenderProgressEvent progressEvent:
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyProgress(progressEvent.Progress));
                        break;
                    default:
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyRenderEvent(parsedEvent).FireAndForget(
                            _logService,
                            nameof(RenderTaskViewModel),
                            RenderLogScope.Task,
                            "后台应用渲染事件失败。"));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logService?.Write(
                RenderLogLevel.Warning,
                RenderLogScope.Task,
                $"解析渲染输出失败: {ex.Message}",
                Id,
                BlendFilePath,
                nameof(RenderTaskViewModel),
                new Dictionary<string, string>
                {
                    ["audience"] = "debug",
                    ["kind"] = "parser_error",
                    ["line"] = line
                });
        }
    }

    internal void HandleRawErrorLine(string line)
    {
        _logService?.Write(
            RenderLogLevel.Debug,
            RenderLogScope.Worker,
            line,
            Id,
            BlendFilePath,
            nameof(RenderTaskViewModel),
            new Dictionary<string, string>
            {
                ["audience"] = "debug",
                ["kind"] = "raw",
                ["stream"] = "stderr"
            });
    }

    internal void ApplyProgress(RenderProgress p)
    {
        if (Status is RenderTaskStatus.Completed or RenderTaskStatus.Failed or RenderTaskStatus.Cancelled)
        {
            return;
        }

        Engine = p.Engine.ToString();
        CurrentFrame = p.CurrentFrame;
        SampleText = p is { SampleCurrent: not null, SampleTotal: not null }
            ? $"{p.SampleCurrent}/{p.SampleTotal}"
            : string.Empty;
        SavedPath = p.SavedPath ?? string.Empty;

        if (p is { SampleCurrent: not null, SampleTotal: > 0 })
        {
            Progress01 = Math.Clamp((double)p.SampleCurrent.Value / p.SampleTotal.Value, 0, 1);
        }
        else
        {
            Progress01 = 0;
        }

        // 计算整体进度（基于实际渲染用的帧范围）
        var totalFrames = RealTotalFrames;
        if (totalFrames > 0)
        {
            // CompletedFrames 只统计已经完全完成的帧，当前正在渲染的帧由 perFrame 表示。
            CompletedFrames = Math.Max(0, p.CurrentFrame - RealStartFrame);
            double perFrame = Progress01; // 当前帧内进度
            OverallProgress01 = Math.Clamp((CompletedFrames + perFrame) / totalFrames, 0, 1);
        }
        else
        {
            OverallProgress01 = 0;
        }

        // 触发进度变化事件
        var frameRenderTime = p.Elapsed ?? TimeSpan.Zero;
        ProgressChanged?.Invoke(this,
            new RenderTaskProgressEventArgs(OverallProgress01, Progress01, p.CurrentFrame, frameRenderTime));
    }

    internal Task ApplyRenderEvent(RenderEvent e)
    {
        try
        {
            if (Status is RenderTaskStatus.Completed or RenderTaskStatus.Failed or RenderTaskStatus.Cancelled)
            {
                return Task.CompletedTask;
            }

            switch (e)
            {
                case RenderSessionStarted s:
                    EnqueueLog(s.IsAnimation ? $"开始动画渲染: {s.StartFrame}..{s.EndFrame}" : $"开始单帧渲染");
                    SetStatus(RenderTaskStatus.Running);
                    break;
                case RenderStarted rs:
                    EnqueueLog($"开始帧 {rs.Frame} ({rs.Engine}) {rs.Scene},{rs.ViewLayer}");
                    break;
                case RenderSaved saved:
                    EnqueueLog($"已保存: {saved.Path} (帧 {saved.Frame})");
                    // 加载渲染完成的图片
                    Task.Run(() => LoadRenderedImageAsync(saved.Path)).FireAndForget(
                        _logService,
                        nameof(RenderTaskViewModel),
                        RenderLogScope.Task,
                        "后台加载已渲染图片失败。");
                    break;
                case RenderCompletedFrame done:
                    EnqueueLog($"帧 {done.Frame} 完成，用时 {done.Time}");
                    break;
                case RenderCompletedAll:
                    EnqueueLog("全部帧完成");
                    OverallProgress01 = 1;
                    // In the worker-host pipeline, final completion is committed only after the
                    // request returns successfully and the output path is verified on disk.
                    break;
                case RenderError err:
                    EnqueueLog($"渲染错误: {err.Message}");
                    // The worker-host pipeline owns retries and final failure transitions.
                    break;
            }
        }
        catch (Exception ex)
        {
            _logService?.Write(
                RenderLogLevel.Error,
                RenderLogScope.Task,
                $"应用渲染事件失败: {ex}",
                Id,
                BlendFilePath,
                nameof(RenderTaskViewModel));
        }

        return Task.CompletedTask;
    }

    private void SetStatus(RenderTaskStatus status)
    {
        Status = status;

        // 当任务开始运行时，初始化CurrentFrame为StartFrame
        if (status == RenderTaskStatus.Running && CurrentFrame == 0)
        {
            CurrentFrame = StartFrame;
        }

        if (status is RenderTaskStatus.Pending or RenderTaskStatus.Completed or RenderTaskStatus.Cancelled)
        {
            StatusDetailText = string.Empty;
        }

        // 触发状态变化事件
        StatusChanged?.Invoke(this, new RenderTaskStatusChangedEventArgs(status, StatusText));
    }

    private void EnqueueLog(string line)
    {
        _logProjection.Enqueue(line);
    }

    public void ResetProgress()
    {
        OverallProgress01 = 0;
        Progress01 = 0;
        CurrentFrame = 0;
        CompletedFrames = 0;
    }

    public void Dispose()
    {
        DetachLogService();
        FileInfo?.Dispose();
        RenderedImage?.Dispose();
    }

    public bool HasTimelineEntries => TimelineEntries.Count > 0;
    public bool HasDebugEntries => DebugEntries.Count > 0;

    partial void OnTimelineEntriesChanged(ObservableCollection<TaskLogEntryViewModel> value)
    {
        OnPropertyChanged(nameof(HasTimelineEntries));
    }

    partial void OnDebugEntriesChanged(ObservableCollection<TaskLogEntryViewModel> value)
    {
        OnPropertyChanged(nameof(HasDebugEntries));
    }

    internal void AttachLogService(IRenderLogService logService)
    {
        _logProjection.Attach(logService);
    }

    internal void DetachLogService()
    {
        _logProjection.Detach();
    }

    private void OnLogAppended(object? sender, RenderLogEvent logEvent)
    {
        _logProjection.OnLogAppended(logEvent);
    }

    private void RebuildLogProjection()
    {
        _logProjection.Rebuild();
    }
}

// 状态变化事件参数
public class RenderTaskStatusChangedEventArgs(RenderTaskStatus status, string statusText) : EventArgs
{
    public RenderTaskStatus Status { get; } = status;
    public string StatusText { get; } = statusText;
}

// 进度变化事件参数
public class RenderTaskProgressEventArgs(
    double overallProgress,
    double currentFrameProgress,
    int currentFrame,
    TimeSpan frameRenderTime)
    : EventArgs
{
    public double OverallProgress { get; } = overallProgress;
    public double CurrentFrameProgress { get; } = currentFrameProgress;
    public int CurrentFrame { get; } = currentFrame;
    public TimeSpan FrameRenderTime { get; } = frameRenderTime;
}

// 请求在Blender中打开文件事件参数
public class OpenInBlenderRequestedEventArgs(string filePath) : EventArgs
{
    public string FilePath { get; } = filePath;
}

// 请求打开文件所在文件夹事件参数
public class OpenSysDirectoryRequestedEventArgs(string filePath) : EventArgs
{
    public string FilePath { get; } = filePath;
}
