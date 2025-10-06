using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services;
using System.Collections.Concurrent;
using System.Threading;
using BlenderRenderQueue.ViewModels;
using Avalonia.Data.Converters;
using System.Globalization;
using BlenderRenderQueue.Models;
using Avalonia.Media.Imaging;
using System.IO;
using System.Linq;
using BlenderRenderQueue.Views;
using BlenderRenderQueue.Converters;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Blender.BlenderProcess;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser;

namespace BlenderRenderQueue.ViewModels;

public partial class RenderTaskViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _blendFilePath = string.Empty;

    [ObservableProperty]
    private int _startFrame = 1;

    [ObservableProperty]
    private int _endFrame = 1;

    [ObservableProperty]
    private bool _animation = true;

    [ObservableProperty]
    private bool _overrideFrameRange = false;

    [ObservableProperty]
    private bool _overrideScene = false;

    [ObservableProperty]
    private string _selectedSceneName = string.Empty;

    [ObservableProperty]
    private bool _autoStart = true;

    [ObservableProperty]
    private bool _enable = true;

    [ObservableProperty]
    private bool _isValid = true;

    // 场景覆写相关属性
    [ObservableProperty]
    private List<string> _availableSceneNames = [];

    public bool HasValidSceneSelection =>
        !string.IsNullOrEmpty(SelectedSceneName) && ScenePropertiesView.SceneNames.Contains(SelectedSceneName);

    public bool ShowSceneOverrideWarning => OverrideScene && !HasValidSceneSelection;

    /// <summary>
    /// 判断覆写场景是否与默认场景相同
    /// </summary>
    public bool IsOverrideSceneSameAsDefault => OverrideScene &&
                                                !string.IsNullOrEmpty(SelectedSceneName) &&
                                                SelectedSceneName == ScenePropertiesView.ActiveSceneName;

    partial void OnEnableChanged(bool value)
    {
        // 当 Enable 属性变化时，触发父级保存数据
        EnableChanged?.Invoke(this, EventArgs.Empty);
        // 更新状态相关的属性
        UpdateStatusDependentProperties();
    }

    partial void OnStatusChanged(RenderTaskStatus value)
    {
        // 更新状态相关的属性
        UpdateStatusDependentProperties();
    }

    partial void OnIsValidChanged(bool value)
    {
        // 更新状态相关的属性
        UpdateStatusDependentProperties();
    }

    [ObservableProperty]
    private bool _isDropTarget = false;

    [ObservableProperty]
    private bool _isDragTarget = false;

    [ObservableProperty]
    private bool _isPendingDeletion = false;


    [ObservableProperty]
    private double _progress01; // 当前帧进度

    [ObservableProperty]
    private double _overallProgress01; // 整体进度

    [ObservableProperty]
    private string _engine = string.Empty;

    [ObservableProperty]
    private int _currentFrame;

    [ObservableProperty]
    private int _completedFrames;

    public int TotalFrames => Math.Max(0, EndFrame - StartFrame + 1);

    // 显示用的帧范围属性
    public int DisplayStartFrame => OverrideFrameRange ? StartFrame : ScenePropertiesView.SceneProperties.FrameStart;
    public int DisplayEndFrame => OverrideFrameRange ? EndFrame : ScenePropertiesView.SceneProperties.FrameEnd;
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
            else if (OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) &&
                     ScenePropertiesView.AllScenes.ContainsKey(SelectedSceneName))
            {
                return ScenePropertiesView.AllScenes[SelectedSceneName].FrameStart;
            }
            else
            {
                return ScenePropertiesView.SceneProperties.FrameStart;
            }
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
            else if (OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) &&
                     ScenePropertiesView.AllScenes.ContainsKey(SelectedSceneName))
            {
                return ScenePropertiesView.AllScenes[SelectedSceneName].FrameEnd;
            }
            else
            {
                return ScenePropertiesView.SceneProperties.FrameEnd;
            }
        }
    }

    public int RealTotalFrames => Math.Max(0, RealEndFrame - RealStartFrame + 1);

    // 任务操作权限相关属性
    /// <summary>
    /// 是否可以修改任务的Enable属性
    /// </summary>
    public bool CanModifyEnable => IsValid && Enable && Status == RenderTaskStatus.Pending;

    /// <summary>
    /// 是否可以修改任务的覆写属性（帧范围、场景等）
    /// </summary>
    public bool CanModifyOverride => IsValid && Enable && Status is RenderTaskStatus.Pending or RenderTaskStatus.Completed;

    /// <summary>
    /// 是否可以删除任务
    /// </summary>
    public bool CanDelete => IsValid && Status == RenderTaskStatus.Pending;

    /// <summary>
    /// 是否应该显示进度条
    /// </summary>
    public bool ShouldShowProgress => IsValid && Status is RenderTaskStatus.Running or RenderTaskStatus.Paused;

    /// <summary>
    /// 获取状态对应的本地化文本键名
    /// </summary>
    public string StatusText => Status.GetLocalizationKey();

    /// <summary>
    /// 统一更新状态相关的属性通知
    /// </summary>
    private void UpdateStatusDependentProperties()
    {
        OnPropertyChanged(nameof(CanModifyEnable));
        OnPropertyChanged(nameof(CanModifyOverride));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(ShouldShowProgress));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// 设置全局渲染超时时间
    /// </summary>
    /// <param name="timeoutSeconds">超时时间（秒）</param>
    public void SetGlobalRenderTimeout(int timeoutSeconds)
    {
        _globalRenderTimeoutSeconds = timeoutSeconds;
    }

    /// <summary>
    /// 设置全局最大重试次数
    /// </summary>
    /// <param name="maxRetryAttempts">最大重试次数</param>
    public void SetGlobalMaxRetryAttempts(int maxRetryAttempts)
    {
        _globalMaxRetryAttempts = maxRetryAttempts;
    }

    /// <summary>
    /// 处理Blender进程退出事件
    /// </summary>
    /// <param name="exitCode">进程退出码</param>
    private void OnBlenderProcessExited(int exitCode)
    {
        // 只在渲染状态时处理进程退出
        if (Status != RenderTaskStatus.Running)
        {
            return;
        }

        EnqueueLog($"Blender进程异常退出，退出码: {exitCode}");

        // 进程异常退出，直接标记任务失败（不进行重试）
        if (exitCode != 0)
        {
            SetStatus(RenderTaskStatus.Failed);
            EndTime = DateTime.Now;
            if (StartTime.HasValue)
            {
                Duration = EndTime.Value - StartTime.Value;
            }
        }
    }


    partial void OnStartFrameChanged(int value)
    {
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(DisplayStartFrame));
        OnPropertyChanged(nameof(DisplayTotalFrames));
        OnPropertyChanged(nameof(RealStartFrame));
        OnPropertyChanged(nameof(RealTotalFrames));
        // 当起始帧变化时，触发父级保存数据
        FrameRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnEndFrameChanged(int value)
    {
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(DisplayEndFrame));
        OnPropertyChanged(nameof(DisplayTotalFrames));
        OnPropertyChanged(nameof(RealEndFrame));
        OnPropertyChanged(nameof(RealTotalFrames));
        // 当结束帧变化时，触发父级保存数据
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
        // 当覆写帧范围状态变化时，触发父级保存数据
        OverrideFrameRangeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnOverrideSceneChanged(bool value)
    {
        // 触发相关属性更新
        OnPropertyChanged(nameof(HasValidSceneSelection));
        OnPropertyChanged(nameof(ShowSceneOverrideWarning));
        OnPropertyChanged(nameof(IsOverrideSceneSameAsDefault));
        OnPropertyChanged(nameof(RealStartFrame));
        OnPropertyChanged(nameof(RealEndFrame));
        OnPropertyChanged(nameof(RealTotalFrames));
        OnPropertyChanged(nameof(FinalSceneProperties));
        OnPropertyChanged(nameof(FramePathDirectory));

        // 触发父级保存数据
        OverrideSceneChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedSceneNameChanged(string value)
    {
        // 触发相关属性更新
        OnPropertyChanged(nameof(HasValidSceneSelection));
        OnPropertyChanged(nameof(ShowSceneOverrideWarning));
        OnPropertyChanged(nameof(IsOverrideSceneSameAsDefault));
        OnPropertyChanged(nameof(RealStartFrame));
        OnPropertyChanged(nameof(RealEndFrame));
        OnPropertyChanged(nameof(RealTotalFrames));
        OnPropertyChanged(nameof(FinalSceneProperties));
        OnPropertyChanged(nameof(FramePathDirectory));

        // 触发父级保存数据
        SceneSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    [ObservableProperty]
    private string _sampleText = string.Empty;

    [ObservableProperty]
    private string _savedPath = string.Empty;

    [ObservableProperty]
    private string _outputLog = string.Empty;

    [ObservableProperty]
    private BlendScenePropertiesViewModel _scenePropertiesView = new();

    partial void OnScenePropertiesViewChanged(BlendScenePropertiesViewModel value)
    {
        // 当ScenePropertiesView变化时，触发相关属性通知
        OnPropertyChanged(nameof(FinalSceneProperties));
        OnPropertyChanged(nameof(FramePathDirectory));

        // 订阅ScenePropertiesView的属性变化事件
        if (value == null) return;
        value.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(value.ActiveSceneProperties) ||
                args.PropertyName == nameof(value.SceneProperties) ||
                args.PropertyName == nameof(value.AllScenes))
            {
                OnPropertyChanged(nameof(FinalSceneProperties));
                OnPropertyChanged(nameof(FramePathDirectory));
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

    /// <summary>
    /// 刷新请求事件
    /// </summary>
    public event EventHandler? RefreshRequested;

    /// <summary>
    /// Enable 属性变化事件
    /// </summary>
    public event EventHandler? EnableChanged;

    /// <summary>
    /// 请求在Blender中打开文件事件
    /// </summary>
    public event EventHandler<OpenInBlenderRequestedEventArgs>? OpenInBlenderRequested;

    /// <summary>
    /// 请求打开文件所在文件夹事件
    /// </summary>
    public event EventHandler<OpenFileDirectoryRequestedEventArgs>? OpenFileDirectoryRequested;

    /// <summary>
    /// 覆写帧范围状态变化事件
    /// </summary>
    public event EventHandler? OverrideFrameRangeChanged;

    /// <summary>
    /// 覆写场景状态变化事件
    /// </summary>
    public event EventHandler? OverrideSceneChanged;

    /// <summary>
    /// 场景选择变化事件
    /// </summary>
    public event EventHandler? SceneSelectionChanged;

    /// <summary>
    /// 帧范围变化事件
    /// </summary>
    public event EventHandler? FrameRangeChanged;

    public string BlendFileName => System.IO.Path.GetFileName(BlendFilePath);

    [ObservableProperty]
    private bool _isLogPaused = false;

    [ObservableProperty]
    private string _logPauseButtonText = "Stop Log";

    // 全局超时设置（从SettingsViewModel获取）
    private int _globalRenderTimeoutSeconds = 300; // 默认5分钟
    private int _globalMaxRetryAttempts = 3; // 默认最大重试3次
    private int _currentFrameRetryAttempts = 0; // 当前帧的重试次数
    private IBlenderProcess? _currentBlenderProcess; // 存储当前的Blender进程实例

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

    /// <summary>
    /// 获取最终渲染场景的属性（考虑场景覆写设置）
    /// </summary>
    public BlendSceneProperties FinalSceneProperties
    {
        get
        {
            // 如果有场景覆写且选择了有效场景，使用覆写场景
            if (OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) &&
                ScenePropertiesView.AllScenes.ContainsKey(SelectedSceneName))
            {
                return ScenePropertiesView.AllScenes[SelectedSceneName];
            }

            // 否则使用默认场景
            return ScenePropertiesView.ActiveSceneProperties;
        }
    }

    /// <summary>
    /// 获取最终渲染场景的帧路径目录，用于绑定到ImageSequencePreviewControl
    /// </summary>
    public string? FramePathDirectory
    {
        get
        {
            var framePath = FinalSceneProperties.FramePath;
            return !string.IsNullOrEmpty(framePath) ? Path.GetDirectoryName(framePath)?.Replace("\\", "/") : null;
        }
    }

    // 保存停止时的进度状态
    private int _lastCompletedFrame = 0;
    private bool _wasStopped = false;

    // 内部状态
    private IRenderSession? _session;
    private IBlenderProcess? _exe;
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly System.Timers.Timer _logTimer;
    private const int MaxLogLines = 1000;
    private int _logLineCount = 0;
    private volatile bool _isFlushing = false;
    private readonly object _logLock = new object();
    private DateTime _lastFlushTime = DateTime.MinValue;
    private const int MinFlushIntervalMs = 50;
    private const int MaxBatchSize = 100;

    // 心跳检查相关
    private DateTime _lastActivityTime = DateTime.UtcNow;

    // 事件
    public event EventHandler<RenderTaskStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<RenderTaskProgressEventArgs>? ProgressChanged;

    public RenderTaskViewModel()
    {
        // 日志批量刷新
        _logTimer = new System.Timers.Timer(200);
        _logTimer.Elapsed += (_, __) => FlushLogQueue();
        _logTimer.AutoReset = true;
        _logTimer.Start();

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
            $"[RenderTaskViewModel] Constructor - File: {Path.GetFileName(blendFilePath)}, IsValid: {IsValid}");
        Console.WriteLine(
            $"[RenderTaskViewModel] Initial ScenePropertiesView state - IsLoading: {ScenePropertiesView.IsLoading}, IsLoaded: {ScenePropertiesView.SceneProperties.IsLoaded}, ShowEmptyState: {ScenePropertiesView.ShowEmptyState}");

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
            Console.WriteLine($"[RenderTaskViewModel] Loading rendered image: {imagePath}");

            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"[RenderTaskViewModel] Rendered image file does not exist: {imagePath}");
                return;
            }

            // 在后台线程加载图片
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    // 使用文件流加载图片，更安全
                    using (var fileStream = File.OpenRead(imagePath))
                    {
                        return new Bitmap(fileStream);
                    }
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
                Console.WriteLine(
                    $"[RenderTaskViewModel] Original image size: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");

                // 释放原始bitmap，因为我们只需要优化版本
                bitmap.Dispose();

                // 直接加载并显示优化版本
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
                                    Console.WriteLine(
                                        $"[RenderTaskViewModel] ✅ Optimized image loaded and displayed: {optimizedBitmap.PixelSize.Width}x{optimizedBitmap.PixelSize.Height}");
                                }
                                else
                                {
                                    Console.WriteLine(
                                        $"[RenderTaskViewModel] ⚠️ Failed to load optimized image, showing placeholder");
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
                // Console.WriteLine($"[RenderTaskViewModel] Loading and optimizing image: {imagePath}");

                // 重新从文件加载图片
                using (var fileStream = File.OpenRead(imagePath))
                {
                    var originalBitmap = new Bitmap(fileStream);
                    var originalSize = originalBitmap.PixelSize;

                    // 如果图片已经小于目标尺寸，直接返回
                    if (originalSize.Width <= maxWidth && originalSize.Height <= maxHeight)
                    {
                        // Console.WriteLine(
                        //     $"[RenderTaskViewModel] Image already optimal size: {originalSize.Width}x{originalSize.Height}");
                        return originalBitmap;
                    }

                    // 计算缩放比例
                    var scaleX = (double)maxWidth / originalSize.Width;
                    var scaleY = (double)maxHeight / originalSize.Height;
                    var scale = Math.Min(scaleX, scaleY);

                    var newWidth = (int)(originalSize.Width * scale);
                    var newHeight = (int)(originalSize.Height * scale);

                    // Console.WriteLine(
                    //     $"[RenderTaskViewModel] Optimizing image from {originalSize.Width}x{originalSize.Height} to {newWidth}x{newHeight}");

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

                    // Console.WriteLine($"[RenderTaskViewModel] Image optimization completed successfully");
                    return renderTarget;
                }
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
        if (string.IsNullOrWhiteSpace(BlendFilePath) || !System.IO.File.Exists(BlendFilePath))
        {
            EnqueueLog("文件路径无效或文件不存在");
            return;
        }

        try
        {
            EnqueueLog("[QUERY] 开始加载文件属性...");
            await ScenePropertiesView.LoadPropertiesAsync(blenderPath, BlendFilePath);

            // 只有在覆写模式下才设置帧范围，否则使用场景默认值
            if (OverrideFrameRange)
            {
                // 如果当前是覆写模式，保持现有的 StartFrame 和 EndFrame 值
                EnqueueLog($"[QUERY] 文件属性加载完成: 使用覆写帧范围 {StartFrame}..{EndFrame}");
            }
            else
            {
                // 非覆写模式，使用场景默认帧范围
                EnqueueLog(
                    $"[QUERY] 文件属性加载完成: 使用场景默认帧范围 {ScenePropertiesView.SceneProperties.FrameStart}..{ScenePropertiesView.SceneProperties.FrameEnd}");
            }

            // 更新场景名称列表, 排除默认场景
            AvailableSceneNames = ScenePropertiesView.SceneNames.ToList();

            // 如果没有覆写场景，设置默认场景名称
            if (!OverrideScene && string.IsNullOrEmpty(SelectedSceneName))
            {
                SelectedSceneName = ScenePropertiesView.ActiveSceneName;
            }

            // 触发显示属性更新
            OnPropertyChanged(nameof(DisplayStartFrame));
            OnPropertyChanged(nameof(DisplayEndFrame));
            OnPropertyChanged(nameof(DisplayTotalFrames));
            OnPropertyChanged(nameof(HasValidSceneSelection));
            OnPropertyChanged(nameof(ShowSceneOverrideWarning));
            OnPropertyChanged(nameof(IsOverrideSceneSameAsDefault));

            // 触发最终场景相关属性更新
            OnPropertyChanged(nameof(FinalSceneProperties));
            OnPropertyChanged(nameof(FramePathDirectory));
        }
        catch (Exception ex)
        {
            EnqueueLog($"[QUERY] 加载文件属性失败: {ex.Message}");
        }
    }

    public async Task StartRenderAsync(IBlenderProcess blenderProcess)
    {
        if (string.IsNullOrWhiteSpace(BlendFilePath))
        {
            EnqueueLog("请先选择 .blend 文件");
            return;
        }

        try
        {
            SetStatus(RenderTaskStatus.Running);
            StartTime = DateTime.Now;

            // 存储当前的Blender进程实例
            _currentBlenderProcess = blenderProcess;

            // 直接使用 IBlenderProcess
            _exe = blenderProcess;
            _exe.OnOutputReceived += HandleRawOutput;
            _exe.OnErrorReceived += HandleRawError;

            // 订阅进程退出事件，用于处理进程异常退出时的重试
            blenderProcess.OnProcessExited += OnBlenderProcessExited;

            _session = new RenderSession(_exe, new RenderOutputParser());
            _session.OnProgress += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProgress(s));
            _session.OnEvent += e => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnEvent(e));

            var cmd = new BlenderCommandService();

            // 为渲染任务设置可配置的超时时间
            // 使用来自SettingsViewModel的超时设置，但确保有合理的最小值
            var renderTimeout = Math.Max(_globalRenderTimeoutSeconds, 300); // 最少5分钟
            // 注意：新架构中，超时管理由RenderTaskViewModel自己处理，不需要设置到IBlenderProcess

            // 根据覆写设置决定是否传递帧范围和场景参数
            string? sceneName = OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) ? SelectedSceneName : null;

            if (OverrideFrameRange)
            {
                EnqueueLog($"开始渲染: {StartFrame}..{EndFrame}, animation={Animation} (无活动超时: {renderTimeout}秒)");
                if (sceneName != null)
                {
                    EnqueueLog($"使用场景覆写: {sceneName}");
                }

                await cmd.StartRenderAsync(_exe, BlendFilePath, Animation, StartFrame, EndFrame, sceneName);
            }
            else
            {
                EnqueueLog($"开始渲染: 使用场景默认帧范围, animation={Animation} (无活动超时: {renderTimeout}秒)");
                if (sceneName != null)
                {
                    EnqueueLog($"使用场景覆写: {sceneName}");
                }

                await cmd.StartRenderAsync(_exe, BlendFilePath, Animation, null, null, sceneName);
            }

            EnqueueLog($"渲染指令已发送完成");
        }
        catch (TaskCanceledException ex)
        {
            if (ex.CancellationToken.IsCancellationRequested)
            {
                EnqueueLog("渲染任务被用户取消");
                SetStatus(RenderTaskStatus.Cancelled);
            }
            else
            {
                EnqueueLog($"渲染任务超时: {ex.Message}");

                // 渲染超时，直接标记任务失败
                SetStatus(RenderTaskStatus.Failed);
            }
        }
        catch (OperationCanceledException ex)
        {
            EnqueueLog($"渲染操作被取消: {ex.Message}");
            SetStatus(RenderTaskStatus.Cancelled);
        }
        catch (Exception ex)
        {
            EnqueueLog($"渲染启动失败: {ex.Message}");
            SetStatus(RenderTaskStatus.Failed);
        }
    }

    public void StopRender()
    {
        // 停止渲染会话和进程
        try
        {
            _session?.Dispose();
        }
        catch
        { }

        _session = null;

        // 停止Blender进程
        if (_exe is not null)
        {
            _exe.OnOutputReceived -= HandleRawOutput;
            _exe.OnErrorReceived -= HandleRawError;
            
            // 停止进程，但不释放，因为它可能被其他任务使用
            try
            {
                Task.Run(async () => await _exe.StopAsync()).Wait(5000); // 5秒超时
            }
            catch
            {
                // 忽略停止进程时的异常
            }
        }

        SetStatus(RenderTaskStatus.Cancelled);
        EndTime = DateTime.Now;
        if (StartTime.HasValue)
        {
            Duration = EndTime.Value - StartTime.Value;
        }

        EnqueueLog("渲染已停止");
    }

    public async Task PauseRenderAsync()
    {
        try
        {
            EnqueueLog("正在暂停渲染...");

            // 立即更新状态，提供即时反馈
            SetStatus(RenderTaskStatus.Paused);
            EnqueueLog($"渲染已暂停，当前帧: {CurrentFrame}");

            // 停止渲染会话
            _session?.Dispose();
            _session = null;

            // 异步停止Blender进程，不阻塞状态更新
            if (_exe is not null)
            {
                _exe.OnOutputReceived -= HandleRawOutput;
                _exe.OnErrorReceived -= HandleRawError;
                
                // 异步停止进程，不等待完成
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _exe.StopAsync();
                    }
                    catch
                    {
                        // 忽略停止进程时的异常
                    }
                });
            }
        }
        catch (Exception ex)
        {
            EnqueueLog($"暂停渲染失败: {ex.Message}");
            SetStatus(RenderTaskStatus.Failed);
        }
    }

    public async Task ResumeRenderAsync(IBlenderProcess blenderProcess, int resumeFromFrame)
    {
        if (string.IsNullOrWhiteSpace(BlendFilePath))
        {
            EnqueueLog("请先选择 .blend 文件");
            return;
        }

        try
        {
            SetStatus(RenderTaskStatus.Running);

            // 存储当前的Blender进程实例
            _currentBlenderProcess = blenderProcess;

            // 直接使用 IBlenderProcess
            _exe = blenderProcess;
            _exe.OnOutputReceived += HandleRawOutput;
            _exe.OnErrorReceived += HandleRawError;

            // 订阅进程退出事件，用于处理进程异常退出时的重试
            blenderProcess.OnProcessExited += OnBlenderProcessExited;

            _session = new RenderSession(_exe, new RenderOutputParser());
            _session.OnProgress += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProgress(s));
            _session.OnEvent += e => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnEvent(e));

            var cmd = new BlenderCommandService();

            // 为渲染任务设置可配置的超时时间
            // 使用来自SettingsViewModel的超时设置，但确保有合理的最小值
            var renderTimeout = Math.Max(_globalRenderTimeoutSeconds, 300); // 最少5分钟
            // 注意：新架构中，超时管理由RenderTaskViewModel自己处理，不需要设置到IBlenderProcess

            // 根据覆写设置决定是否传递帧范围和场景参数
            string? sceneName = OverrideScene && !string.IsNullOrEmpty(SelectedSceneName) ? SelectedSceneName : null;

            // 从指定帧开始渲染
            var startFrame = OverrideFrameRange ? Math.Max(StartFrame, resumeFromFrame) : resumeFromFrame;
            var endFrame = OverrideFrameRange ? EndFrame : ScenePropertiesView.SceneProperties.FrameEnd;

            EnqueueLog($"恢复渲染: 从帧 {startFrame} 开始到 {endFrame}, animation={Animation} (无活动超时: {renderTimeout}秒)");
            if (sceneName != null)
            {
                EnqueueLog($"使用场景覆写: {sceneName}");
            }

            await cmd.StartRenderAsync(_exe, BlendFilePath, Animation, startFrame, endFrame, sceneName);

            EnqueueLog($"恢复渲染指令已发送完成");
        }
        catch (TaskCanceledException ex)
        {
            if (ex.CancellationToken.IsCancellationRequested)
            {
                EnqueueLog("恢复渲染任务被用户取消");
                SetStatus(RenderTaskStatus.Cancelled);
            }
            else
            {
                EnqueueLog($"恢复渲染任务超时: {ex.Message}");

                // 恢复渲染超时，直接标记任务失败
                SetStatus(RenderTaskStatus.Failed);
            }
        }
        catch (OperationCanceledException ex)
        {
            EnqueueLog($"恢复渲染操作被取消: {ex.Message}");
            SetStatus(RenderTaskStatus.Cancelled);
        }
        catch (Exception ex)
        {
            EnqueueLog($"恢复渲染启动失败: {ex.Message}");
            SetStatus(RenderTaskStatus.Failed);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        OutputLog = string.Empty;
        _logLineCount = 0;
        // 清空队列中的待处理日志
        while (_logQueue.TryDequeue(out _))
        { }

        EnqueueLog("日志已清空");
    }

    [RelayCommand]
    private void ToggleLogPause()
    {
        IsLogPaused = !IsLogPaused;
        LogPauseButtonText = IsLogPaused ? "Resume Log" : "Stop Log";
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

            // 触发事件，请求父级打开文件所在文件夹
            OpenFileDirectoryRequested?.Invoke(this, new OpenFileDirectoryRequestedEventArgs(BlendFilePath));
        }
        catch (Exception ex)
        {
            EnqueueLog($"[ERROR] 打开文件夹失败: {ex.Message}");
        }
    }

    private void HandleRawOutput(string line)
    {
        EnqueueLog($"[OUT] {line}");
    }

    private void HandleRawError(string line)
    {
        EnqueueLog($"[ERR] {line}");

        // 检查是否是超时错误，如果是，不要立即设置为失败状态
        if (line.Contains("操作超时") && line.Contains("render"))
        {
            // 对于渲染超时，只记录日志，不改变状态
            EnqueueLog("[INFO] 检测到渲染超时，但渲染进程可能仍在继续...");
            return;
        }

        // 其他错误仍然会触发RenderError事件
    }

    private void OnProgress(RenderProgress p)
    {
        Engine = p.Engine.ToString();
        CurrentFrame = p.CurrentFrame;
        SampleText = p.SampleCurrent.HasValue && p.SampleTotal.HasValue
            ? $"{p.SampleCurrent}/{p.SampleTotal}"
            : string.Empty;
        SavedPath = p.SavedPath ?? string.Empty;

        if (p.SampleCurrent.HasValue && p.SampleTotal.HasValue && p.SampleTotal.Value > 0)
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
            CompletedFrames = Math.Max(0, p.CurrentFrame - RealStartFrame + 1);
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

    private async void OnEvent(RenderEvent e)
    {
        switch (e)
        {
            case RenderSessionStarted s:
                EnqueueLog(s.IsAnimation ? $"开始动画渲染: {s.StartFrame}..{s.EndFrame}" : $"开始单帧渲染");
                SetStatus(RenderTaskStatus.Running);
                break;
            case RenderStarted rs:
                EnqueueLog($"开始帧 {rs.Frame} ({rs.Engine}) {rs.Scene},{rs.ViewLayer}");
                // 重置当前帧的重试计数器
                _currentFrameRetryAttempts = 0;
                break;
            case RenderSaved saved:
                EnqueueLog($"已保存: {saved.Path} (帧 {saved.Frame})");
                // 加载渲染完成的图片
                _ = Task.Run(async () => await LoadRenderedImageAsync(saved.Path));
                break;
            case RenderCompletedFrame done:
                EnqueueLog($"帧 {done.Frame} 完成，用时 {done.Time}");
                // 帧完成时重置帧重试计数器
                _currentFrameRetryAttempts = 0;
                break;
            case RenderCompletedAll:
                EnqueueLog("全部帧完成");
                OverallProgress01 = 1;

                // 先设置状态，确保状态变化事件被触发
                SetStatus(RenderTaskStatus.Completed);
                EndTime = DateTime.Now;
                if (StartTime.HasValue)
                {
                    Duration = EndTime.Value - StartTime.Value;
                }

                // 渲染完成后，停止 Blender 进程
                try
                {
                    // 先取消事件订阅，避免在停止过程中触发事件
                    if (_exe is not null)
                    {
                        _exe.OnOutputReceived -= HandleRawOutput;
                        _exe.OnErrorReceived -= HandleRawError;
                    }

                    _session?.Dispose();
                    _session = null;
                }
                catch (Exception ex)
                {
                    EnqueueLog($"停止渲染进程时出错: {ex.Message}");
                }

                break;
            case RenderError err:
                EnqueueLog($"渲染错误: {err.Message}");

                // 尝试帧级别的重试
                if (_currentFrameRetryAttempts < _globalMaxRetryAttempts && _currentBlenderProcess != null)
                {
                    _currentFrameRetryAttempts++;
                    EnqueueLog($"检测到帧渲染错误，尝试第 {_currentFrameRetryAttempts} 次重试 (最大 {_globalMaxRetryAttempts} 次)...");

                    try
                    {
                        // 等待一小段时间再重试当前帧
                        await Task.Delay(2000);

                        // 重新开始当前帧的渲染
                        await ResumeRenderAsync(_currentBlenderProcess, CurrentFrame);
                        EnqueueLog($"第 {_currentFrameRetryAttempts} 次帧重试开始成功");
                        return; // 重试成功，继续渲染
                    }
                    catch (Exception ex)
                    {
                        EnqueueLog($"第 {_currentFrameRetryAttempts} 次帧重试失败: {ex.Message}");
                    }
                }

                // 帧级别重试失败，标记任务失败
                SetStatus(RenderTaskStatus.Failed);
                EndTime = DateTime.Now;
                if (StartTime.HasValue)
                {
                    Duration = EndTime.Value - StartTime.Value;
                }

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

        // 当任务完成时，重置帧重试计数器
        if (status == RenderTaskStatus.Completed)
        {
            _currentFrameRetryAttempts = 0;
        }

        // 触发状态变化事件
        StatusChanged?.Invoke(this, new RenderTaskStatusChangedEventArgs(status, StatusText));
    }

    private void EnqueueLog(string line)
    {
        if (IsLogPaused) return;

        // 简单的重复日志过滤
        if (!string.IsNullOrWhiteSpace(line) && _logQueue.Count < 500) // 防止队列过大
        {
            _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
        }
    }

    private void FlushLogQueue()
    {
        if (_logQueue.IsEmpty || IsLogPaused || _isFlushing) return;

        // 防止频繁刷新
        var now = DateTime.Now;
        if ((now - _lastFlushTime).TotalMilliseconds < MinFlushIntervalMs) return;

        lock (_logLock)
        {
            if (_isFlushing) return;
            _isFlushing = true;
        }

        try
        {
            var sb = new StringBuilder();
            int dequeued = 0;

            // 限制单次处理的日志数量，避免UI阻塞
            while (_logQueue.TryDequeue(out var line) && dequeued < MaxBatchSize)
            {
                if (dequeued++ > 0) sb.AppendLine();
                sb.Append(line);
            }

            var text = sb.ToString();
            if (string.IsNullOrEmpty(text)) return;

            _lastFlushTime = now;

            // 使用低优先级调度，减少对UI的影响
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { UpdateOutputLog(text); },
                Avalonia.Threading.DispatcherPriority.Background);
        }
        finally
        {
            _isFlushing = false;
        }
    }

    private void UpdateOutputLog(string newText)
    {
        // 将新文本追加到现有日志，并按行数限制截断最旧部分
        if (string.IsNullOrEmpty(OutputLog))
        {
            OutputLog = newText;
            _logLineCount = CountLines(OutputLog);
        }
        else
        {
            OutputLog += Environment.NewLine + newText;
            _logLineCount += CountLines(newText);
        }

        if (_logLineCount > MaxLogLines)
        {
            // 只保留最后 MaxLogLines 行，使用更高效的字符串操作
            var lines = OutputLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var start = Math.Max(0, lines.Length - MaxLogLines);
            OutputLog = string.Join(Environment.NewLine, lines, start, lines.Length - start);
            _logLineCount = MaxLogLines;
        }
    }

    private static int CountLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int count = 1;
        for (int i = 0; i < s.Length; i++)
            if (s[i] == '\n')
                count++;
        return count;
    }

    private void DisposeSession()
    {
        try
        {
            _session?.Dispose();
        }
        catch
        { }

        if (_exe is not null)
        {
            _exe.OnOutputReceived -= HandleRawOutput;
            _exe.OnErrorReceived -= HandleRawError;
            // 注意：不释放_exe，因为它可能被其他任务使用
        }

        _session = null;
        _exe = null;
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
        _logTimer?.Stop();
        _logTimer?.Dispose();
        DisposeSession();
        FileInfo?.Dispose();
        RenderedImage?.Dispose();
    }
}

// 状态变化事件参数
public class RenderTaskStatusChangedEventArgs : EventArgs
{
    public RenderTaskStatus Status { get; }
    public string StatusText { get; }

    public RenderTaskStatusChangedEventArgs(RenderTaskStatus status, string statusText)
    {
        Status = status;
        StatusText = statusText;
    }
}

// 进度变化事件参数
public class RenderTaskProgressEventArgs : EventArgs
{
    public double OverallProgress { get; }
    public double CurrentFrameProgress { get; }
    public int CurrentFrame { get; }
    public TimeSpan FrameRenderTime { get; }

    public RenderTaskProgressEventArgs(double overallProgress, double currentFrameProgress, int currentFrame,
        TimeSpan frameRenderTime)
    {
        OverallProgress = overallProgress;
        CurrentFrameProgress = currentFrameProgress;
        CurrentFrame = currentFrame;
        FrameRenderTime = frameRenderTime;
    }
}

// 请求在Blender中打开文件事件参数
public class OpenInBlenderRequestedEventArgs : EventArgs
{
    public string FilePath { get; }

    public OpenInBlenderRequestedEventArgs(string filePath)
    {
        FilePath = filePath;
    }
}

// 请求打开文件所在文件夹事件参数
public class OpenFileDirectoryRequestedEventArgs : EventArgs
{
    public string FilePath { get; }

    public OpenFileDirectoryRequestedEventArgs(string filePath)
    {
        FilePath = filePath;
    }
}