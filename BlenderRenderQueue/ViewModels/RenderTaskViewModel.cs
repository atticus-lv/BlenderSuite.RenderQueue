using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Models;
using Avalonia.Media.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using BlenderRenderQueue.Views;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Blender.BlenderProcess;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.UI;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Localizer;
using BlenderRenderQueue.ViewModels.Logs;

namespace BlenderRenderQueue.ViewModels;

public partial class RenderTaskViewModel : ViewModelBase
{
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
        Console.WriteLine(
            $"[RenderTaskViewModel] Queue running state changed to: {isQueueRunning}, CanRefresh: {CanRefresh}");
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
    private ObservableCollection<TaskLogEntryViewModel> _timelineEntries = [];

    [ObservableProperty]
    private ObservableCollection<TaskLogEntryViewModel> _debugEntries = [];

    [ObservableProperty]
    private string _debugLogText = string.Empty;

    [ObservableProperty]
    private BlendScenePropertiesViewModel _scenePropertiesView = new();

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

            Console.WriteLine($"[RenderTaskViewModel] ✅ Image preview window opened: {RenderedImagePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderTaskViewModel] Error opening image preview: {ex.Message}");
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
        if (string.IsNullOrWhiteSpace(BlendFilePath) || !System.IO.File.Exists(BlendFilePath))
        {
            EnqueueLog("文件路径无效或文件不存在，无法刷新");
            return;
        }

        try
        {
            EnqueueLog("[REFRESH] 开始刷新文件属性...");

            var currentOverrideFrameRange = OverrideFrameRange;
            var currentStartFrame = StartFrame;
            var currentEndFrame = EndFrame;
            var currentOverrideScene = OverrideScene;
            var currentSelectedSceneName = SelectedSceneName;
            var currentEnable = Enable;

            Console.WriteLine(
                $"[RenderTaskViewModel] Refreshing file properties - preserving overrides: FrameRange={currentOverrideFrameRange} ({currentStartFrame}-{currentEndFrame}), Scene={currentOverrideScene} ({currentSelectedSceneName}), Enable={currentEnable}");

            await ScenePropertiesView.LoadPropertiesAsync(blenderPath, BlendFilePath);

            OverrideFrameRange = currentOverrideFrameRange;
            StartFrame = currentStartFrame;
            EndFrame = currentEndFrame;
            OverrideScene = currentOverrideScene;
            SelectedSceneName = currentSelectedSceneName;
            Enable = currentEnable;

            AvailableSceneNames = ScenePropertiesView.SceneNames.ToList();

            if (!OverrideScene && string.IsNullOrEmpty(SelectedSceneName))
            {
                SelectedSceneName = ScenePropertiesView.SelectedSceneName ?? string.Empty;
            }

            OnPropertyChanged(nameof(DisplayStartFrame));
            OnPropertyChanged(nameof(DisplayEndFrame));
            OnPropertyChanged(nameof(DisplayTotalFrames));
            OnPropertyChanged(nameof(RealStartFrame));
            OnPropertyChanged(nameof(RealEndFrame));
            OnPropertyChanged(nameof(RealTotalFrames));
            OnPropertyChanged(nameof(AvailableSceneNames));
            OnPropertyChanged(nameof(HasValidSceneSelection));
            OnPropertyChanged(nameof(ShowSceneOverrideWarning));
            OnPropertyChanged(nameof(IsOverrideSceneIsDefaultScene));
            OnPropertyChanged(nameof(FinalSceneProperties));
            OnPropertyChanged(nameof(FramePathDirectory));

            // 重新加载文件信息
            LoadFileInfo();

            EnqueueLog("[REFRESH] 文件属性刷新完成");
            Console.WriteLine($"[RenderTaskViewModel] ✅ File properties refreshed successfully - overrides preserved");
        }
        catch (Exception ex)
        {
            EnqueueLog($"[REFRESH] 刷新文件属性失败: {ex.Message}");
            Console.WriteLine($"[RenderTaskViewModel] ❌ Failed to refresh file properties: {ex.Message}");
        }
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

    [ObservableProperty]
    private BlendScenePropertiesViewModel _scenePropertiesViewModel = new();


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

    // 事件
    public event EventHandler<RenderTaskStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<RenderTaskProgressEventArgs>? ProgressChanged;

    public RenderTaskViewModel()
    {
        // 手动触发ScenePropertiesView的初始化
        OnScenePropertiesViewChanged(ScenePropertiesView);
    }

    public RenderTaskViewModel(string blendFilePath, int startFrame, int endFrame, bool animation = true,
        bool overrideFrameRange = false) : this()
    {
        BlendFilePath = blendFilePath;
        StartFrame = startFrame;
        EndFrame = endFrame;
        Animation = animation;
        OverrideFrameRange = overrideFrameRange;

        // 检查文件有效性
        IsValid = !string.IsNullOrEmpty(blendFilePath) && File.Exists(blendFilePath);

        Console.WriteLine(
            $"[RenderTaskViewModel] Constructor - ID: {Id}, File: {Path.GetFileName(blendFilePath)}, IsValid: {IsValid}");
        Console.WriteLine(
            $"[RenderTaskViewModel] Initial ScenePropertiesView state - IsLoading: {ScenePropertiesView.IsLoading}, IsLoaded: {ScenePropertiesView.SceneProperties.IsLoaded}, ShowEmptyState: {ScenePropertiesView.ShowEmptyState}");

        // 加载文件信息
        LoadFileInfo();
    }

    /// <summary>
    /// 从 RenderTaskInfo 数据创建 RenderTaskViewModel 实例
    /// 如果 RenderTaskInfo 中有 UUID 则使用，否则生成新的
    /// </summary>
    public RenderTaskViewModel(RenderTaskInfo taskInfo) : this()
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

        Console.WriteLine(
            $"[RenderTaskViewModel] Constructor from RenderTaskInfo - ID: {Id}, File: {Path.GetFileName(BlendFilePath)}, IsValid: {IsValid}");

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
        try
        {
            // Console.WriteLine($"[RenderTaskViewModel] Loading rendered image: {imagePath}");

            if (!File.Exists(imagePath))
            {
                // Console.WriteLine($"[RenderTaskViewModel] Rendered image file does not exist: {imagePath}");
                return;
            }

            // 在后台线程加载图片
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    // 使用文件流加载图片，更安全
                    using var fileStream = File.OpenRead(imagePath);
                    return new Bitmap(fileStream);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderTaskViewModel] Error loading image: {ex.Message}");
                    Console.WriteLine($"[RenderTaskViewModel] Stack trace: {ex.StackTrace}");
                    return null;
                }
            });

            if (bitmap != null)
            {
                // Console.WriteLine(
                //     $"[RenderTaskViewModel] Original image size: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");

                bitmap.Dispose();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var optimizedBitmap = await LoadAndOptimizeImageAsync(imagePath, 120, 90);

                        // 在UI线程更新为优化后的图片
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (optimizedBitmap != null)
                                {
                                    RenderedImage?.Dispose();
                                    RenderedImage = optimizedBitmap;
                                    RenderedImagePath = imagePath;
                                    HasRenderedImage = true;
                                }
                                else
                                {
                                    HasRenderedImage = false;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[RenderTaskViewModel] Error setting optimized image: {ex.Message}");
                                HasRenderedImage = false;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RenderTaskViewModel] Error loading optimized image: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderTaskViewModel] Error in LoadRenderedImageAsync: {ex.Message}");
        }
    }

    /// <summary>
    /// 从文件加载并优化图片尺寸
    /// </summary>
    private async Task<Bitmap?> LoadAndOptimizeImageAsync(string imagePath, int maxWidth, int maxHeight)
    {
        return await Task.Run(() =>
        {
            try
            {
                // 重新从文件加载图片
                using var fileStream = File.OpenRead(imagePath);
                var originalBitmap = new Bitmap(fileStream);
                var originalSize = originalBitmap.PixelSize;

                // 如果图片已经小于目标尺寸，直接返回
                if (originalSize.Width <= maxWidth && originalSize.Height <= maxHeight)
                {
                    return originalBitmap;
                }

                // 计算缩放比例
                var scaleX = (double)maxWidth / originalSize.Width;
                var scaleY = (double)maxHeight / originalSize.Height;
                var scale = Math.Min(scaleX, scaleY);

                var newWidth = (int)(originalSize.Width * scale);
                var newHeight = (int)(originalSize.Height * scale);


                // 使用RenderTargetBitmap进行缩放
                var renderTarget = new RenderTargetBitmap(new Avalonia.PixelSize(newWidth, newHeight));
                using (var drawingContext = renderTarget.CreateDrawingContext())
                {
                    var sourceRect = new Avalonia.Rect(0, 0, originalSize.Width, originalSize.Height);
                    var destRect = new Avalonia.Rect(0, 0, newWidth, newHeight);

                    drawingContext.DrawImage(originalBitmap, sourceRect, destRect);
                }

                // 释放原始位图
                originalBitmap.Dispose();

                return renderTarget;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderTaskViewModel] Error loading and optimizing image: {ex.Message}");
                Console.WriteLine($"[RenderTaskViewModel] Stack trace: {ex.StackTrace}");
                return null;
            }
        });
    }

    public async Task LoadFilePropertiesAsync(string blenderPath)
    {
        if (string.IsNullOrWhiteSpace(BlendFilePath) || !File.Exists(BlendFilePath))
        {
            EnqueueLog("文件路径无效或文件不存在");
            return;
        }

        try
        {
            EnqueueLog("[QUERY] 开始加载文件属性...");
            await ScenePropertiesView.LoadPropertiesAsync(blenderPath, BlendFilePath);

            // 只有在覆写模式下才设置帧范围，否则使用场景默认值
            // 如果当前是覆写模式，保持现有的 StartFrame 和 EndFrame 值
            EnqueueLog(OverrideFrameRange
                ? $"[QUERY] 文件属性加载完成: 使用覆写帧范围 {StartFrame}..{EndFrame}"
                // 非覆写模式，使用场景默认帧范围
                : $"[QUERY] 文件属性加载完成: 使用场景默认帧范围 {ScenePropertiesView.SceneProperties.FrameStart}..{ScenePropertiesView.SceneProperties.FrameEnd}");

            // 更新场景名称列表, 排除默认场景
            AvailableSceneNames = ScenePropertiesView.SceneNames.ToList();

            // 如果没有覆写场景，设置默认场景名称
            if (!OverrideScene && string.IsNullOrEmpty(SelectedSceneName))
            {
                SelectedSceneName = ScenePropertiesView.SelectedSceneName ?? string.Empty;
            }

            // 触发显示属性更新
            OnPropertyChanged(nameof(DisplayStartFrame));
            OnPropertyChanged(nameof(DisplayEndFrame));
            OnPropertyChanged(nameof(DisplayTotalFrames));
            OnPropertyChanged(nameof(RealStartFrame));
            OnPropertyChanged(nameof(RealEndFrame));
            OnPropertyChanged(nameof(RealTotalFrames));
            OnPropertyChanged(nameof(AvailableSceneNames));
            OnPropertyChanged(nameof(HasValidSceneSelection));
            OnPropertyChanged(nameof(ShowSceneOverrideWarning));
            OnPropertyChanged(nameof(IsOverrideSceneIsDefaultScene));

            // 触发最终场景相关属性更新
            OnPropertyChanged(nameof(FinalSceneProperties));
            OnPropertyChanged(nameof(FramePathDirectory));
        }
        catch (Exception ex)
        {
            EnqueueLog($"[QUERY] 加载文件属性失败: {ex.Message}");
        }
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
        _logClearCutoff = DateTimeOffset.UtcNow;
        TimelineEntries.Clear();
        DebugEntries.Clear();
        DebugLogText = string.Empty;
        OutputLog = string.Empty;
        OnPropertyChanged(nameof(HasTimelineEntries));
        OnPropertyChanged(nameof(HasDebugEntries));
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
        try
        {
            // 检查 Blender 是否可用
            if (_processService == null)
            {
                EnqueueLog("[ERROR] " + Localizer.Localizer.Instance["VideoGeneration_ServiceUnavailable"]);
                this.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                    Localizer.Localizer.Instance["VideoGeneration_ServiceUnavailable"]);
                return;
            }

            // 获取帧路径目录
            var framePath = FinalSceneProperties.FramePath;
            if (string.IsNullOrEmpty(framePath))
            {
                EnqueueLog("[ERROR] " + Localizer.Localizer.Instance["VideoGeneration_NoFramePath"]);
                this.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                    Localizer.Localizer.Instance["VideoGeneration_NoFramePath"]);
                return;
            }

            var frameDirectory = Path.GetDirectoryName(framePath);
            if (string.IsNullOrEmpty(frameDirectory) || !Directory.Exists(frameDirectory))
            {
                EnqueueLog("[ERROR] " +
                           string.Format(Localizer.Localizer.Instance["VideoGeneration_FramePathNotExists"],
                               frameDirectory));
                this.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                    string.Format(Localizer.Localizer.Instance["VideoGeneration_FramePathNotExists"], frameDirectory));
                return;
            }

            // 检查目录中是否有图片文件
            var supportedExtensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tiff", "*.tga" };
            var hasImages = supportedExtensions.Any(ext =>
                Directory.GetFiles(frameDirectory, ext, SearchOption.TopDirectoryOnly).Length > 0);

            if (!hasImages)
            {
                EnqueueLog("[ERROR] " +
                           string.Format(Localizer.Localizer.Instance["VideoGeneration_NoImagesInFramePath"],
                               frameDirectory));
                this.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                    string.Format(Localizer.Localizer.Instance["VideoGeneration_NoImagesInFramePath"], frameDirectory));
                return;
            }

            // 获取帧率
            var fps = FinalSceneProperties.Fps ?? 24.0; // 默认 24fps

            // 生成输出视频路径：与输入目录同名，放在同一层级
            var inputDirectoryName = Path.GetFileName(frameDirectory);
            var parentDirectory = Path.GetDirectoryName(frameDirectory);
            var outputVideoPath = Path.Combine(parentDirectory ?? "", $"{inputDirectoryName}.mp4");

            // 开始生成视频
            IsGeneratingVideo = true;
            VideoGenerationProgress = 0.0;
            VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Starting"];
            EnqueueLog(string.Format(Localizer.Localizer.Instance["VideoGeneration_LogStarting"], outputVideoPath));

            // 显示进度 Toast
            var progressBar = new ProgressBar
            {
                Value = 0,
                ShowProgressText = true,
                Minimum = 0,
                Maximum = 100
            };
            var fileName = Path.GetFileName(BlendFilePath);
            var titleName = fileName.EndsWith(".blend", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - 6)
                : fileName;
            var progressToast =
                this.ShowProgressToast(
                    string.Format(Localizer.Localizer.Instance["VideoGeneration_ToastTitle"],
                        titleName), progressBar);

            // 使用进程管理服务创建视频生成进程
            var videoProcess = await _processService.CreateVideoProcessAsync();
            var success = false;

            try
            {
                // 创建视频服务
                var tempVideoService = new BlenderVideoService(videoProcess);

                // 使用Blender生成视频
                success = await tempVideoService.GenerateVideoFromImagesAsync(
                    frameDirectory,
                    outputVideoPath,
                    fps,
                    _videoCodec,
                    _videoQuality,
                    progress =>
                    {
                        // 更新进度
                        VideoGenerationProgress = progress;
                        VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Generating"];

                        // 更新进度 Toast
                        progressToast?.UpdateProgressToast(progress);
                    });
            }
            finally
            {
                // 视频生成完成后停止并释放进程
                await videoProcess.StopAsync();
                _processService.UnregisterProcess(videoProcess.ProcessId);
                videoProcess.Dispose();
            }

            if (success)
            {
                VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Completed"];
                EnqueueLog(string.Format(Localizer.Localizer.Instance["VideoGeneration_LogSuccess"], outputVideoPath));

                // 在UI线程上处理Toast显示
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // 关闭进度 Toast
                    this.DismissToast(progressToast);

                    // 显示成功 Toast
                    this.ShowSuccessToast(Localizer.Localizer.Instance["VideoGeneration_SuccessTitle"],
                        string.Format(Localizer.Localizer.Instance["VideoGeneration_SuccessMessage"],
                            Path.GetFileName(BlendFilePath)));
                });

                // 自动打开视频所在位置
                if (!string.IsNullOrEmpty(outputVideoPath) && File.Exists(outputVideoPath))
                {
                    // 延迟一点时间再打开，让用户看到toast通知
                    _ = Task.Delay(1000).ContinueWith(_ =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            var success = FileSystemHelper.OpenFileDirectory(outputVideoPath);
                            if (!success)
                            {
                                this.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_OpenFailed"],
                                    Localizer.Localizer.Instance["VideoGeneration_CannotOpenLocation"]);
                            }
                        });
                    });
                }
            }
            else
            {
                VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Failed"];
                EnqueueLog(Localizer.Localizer.Instance["VideoGeneration_LogFailed"]);

                // 在UI线程上处理Toast显示
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // 关闭进度 Toast
                    this.DismissToast(progressToast);

                    // 显示失败 Toast
                    this.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                        Localizer.Localizer.Instance["VideoGeneration_ErrorMessage"]);
                });
            }
        }
        catch (Exception ex)
        {
            VideoGenerationStatus = string.Format(Localizer.Localizer.Instance["VideoGeneration_Error"], ex.Message);
            EnqueueLog(string.Format(Localizer.Localizer.Instance["VideoGeneration_LogError"], ex.Message));

            // 在UI线程上处理Toast显示
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                this.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                    string.Format(Localizer.Localizer.Instance["VideoGeneration_ErrorMessageWithDetails"],
                        ex.Message));
            });
        }
        finally
        {
            IsGeneratingVideo = false;
        }
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
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyRenderEvent(parsedEvent));
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

    internal async void ApplyRenderEvent(RenderEvent e)
    {
        if (Status is RenderTaskStatus.Completed or RenderTaskStatus.Failed or RenderTaskStatus.Cancelled)
        {
            return;
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
                _ = Task.Run(async () => await LoadRenderedImageAsync(saved.Path));
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
        if (IsLogPaused || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var (level, scope, message, audience, kind) = ClassifyLogLine(line);
        if (_logService != null)
        {
            _logService.Write(
                level,
                scope,
                message,
                Id,
                BlendFilePath,
                nameof(RenderTaskViewModel),
                new Dictionary<string, string>
                {
                    ["audience"] = audience,
                    ["kind"] = kind
                });
            return;
        }

        var fallbackLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (string.Equals(audience, "debug", StringComparison.Ordinal))
        {
            DebugLogText = string.IsNullOrWhiteSpace(DebugLogText)
                ? fallbackLine
                : $"{DebugLogText}{Environment.NewLine}{fallbackLine}";
            OutputLog = DebugLogText;
        }
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
        if (ReferenceEquals(_logService, logService))
        {
            return;
        }

        DetachLogService();
        _logService = logService;
        _logService.LogAppended += OnLogAppended;
        RebuildLogProjection();
    }

    internal void DetachLogService()
    {
        if (_logService == null)
        {
            return;
        }

        _logService.LogAppended -= OnLogAppended;
        _logService = null;
    }

    private void OnLogAppended(object? sender, RenderLogEvent logEvent)
    {
        if (!ShouldIncludeForTask(logEvent))
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!ShouldIncludeForTask(logEvent))
            {
                return;
            }

            if (ShouldIncludeInTimeline(logEvent))
            {
                TimelineEntries.Insert(0, new TaskLogEntryViewModel(logEvent));
            }

            if (ShouldIncludeInDebug(logEvent))
            {
                var debugEntry = new TaskLogEntryViewModel(logEvent);
                DebugEntries.Insert(0, debugEntry);
                AppendDebugText(debugEntry);
            }

            OnPropertyChanged(nameof(HasTimelineEntries));
            OnPropertyChanged(nameof(HasDebugEntries));
        });
    }

    private void RebuildLogProjection()
    {
        TimelineEntries.Clear();
        DebugEntries.Clear();
        DebugLogText = string.Empty;
        OutputLog = string.Empty;

        if (_logService == null)
        {
            return;
        }

        var events = _logService.GetEvents(new RenderLogProjection
        {
            TaskId = Id,
            IncludeDebug = true,
            IncludeRaw = true
        });

        foreach (var logEvent in events.Reverse())
        {
            if (!ShouldIncludeForTask(logEvent))
            {
                continue;
            }

            if (ShouldIncludeInTimeline(logEvent))
            {
                TimelineEntries.Insert(0, new TaskLogEntryViewModel(logEvent));
            }

            if (ShouldIncludeInDebug(logEvent))
            {
                DebugEntries.Insert(0, new TaskLogEntryViewModel(logEvent));
            }
        }

        RefreshDebugText();
        OnPropertyChanged(nameof(HasTimelineEntries));
        OnPropertyChanged(nameof(HasDebugEntries));
    }

    private bool ShouldIncludeForTask(RenderLogEvent logEvent)
    {
        if (logEvent.TaskId != Id)
        {
            return false;
        }

        return !_logClearCutoff.HasValue || logEvent.Timestamp >= _logClearCutoff.Value;
    }

    private static bool ShouldIncludeInTimeline(RenderLogEvent logEvent)
    {
        if (logEvent.Level == RenderLogLevel.Debug)
        {
            return false;
        }

        return !logEvent.Metadata.TryGetValue("audience", out var audience) ||
               !string.Equals(audience, "debug", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludeInDebug(RenderLogEvent logEvent)
    {
        if (logEvent.Level == RenderLogLevel.Debug)
        {
            return true;
        }

        return logEvent.Metadata.TryGetValue("audience", out var audience) &&
               string.Equals(audience, "debug", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshDebugText()
    {
        DebugLogText = string.Join(
            Environment.NewLine,
            DebugEntries
                .OrderBy(entry => entry.Event.Timestamp)
                .Select(FormatDebugLine));
        OutputLog = DebugLogText;
    }

    private void AppendDebugText(TaskLogEntryViewModel entry)
    {
        var line = FormatDebugLine(entry);
        DebugLogText = string.IsNullOrEmpty(DebugLogText)
            ? line
            : string.Concat(DebugLogText, Environment.NewLine, line);
        OutputLog = DebugLogText;
    }

    private static string FormatDebugLine(TaskLogEntryViewModel entry)
    {
        return $"[{entry.Event.Timestamp.ToLocalTime():HH:mm:ss}] [{entry.LevelText}] {entry.Message}";
    }

    private static (RenderLogLevel Level, RenderLogScope Scope, string Message, string Audience, string Kind)
        ClassifyLogLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
        {
            return (RenderLogLevel.Error, RenderLogScope.System, trimmed[7..].Trim(), "timeline", "message");
        }

        if (trimmed.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase))
        {
            return (RenderLogLevel.Warning, RenderLogScope.System, trimmed[6..].Trim(), "debug", "message");
        }

        if (trimmed.StartsWith("[QUERY]", StringComparison.OrdinalIgnoreCase))
        {
            return (RenderLogLevel.Info, RenderLogScope.Task, trimmed[7..].Trim(), "timeline", "message");
        }

        if (trimmed.StartsWith("[REFRESH]", StringComparison.OrdinalIgnoreCase))
        {
            return (RenderLogLevel.Info, RenderLogScope.Task, trimmed[9..].Trim(), "timeline", "message");
        }

        if (trimmed.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase))
        {
            return (RenderLogLevel.Info, RenderLogScope.System, trimmed[6..].Trim(), "timeline", "message");
        }

        return (RenderLogLevel.Info, RenderLogScope.Task, trimmed, "timeline", "message");
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
