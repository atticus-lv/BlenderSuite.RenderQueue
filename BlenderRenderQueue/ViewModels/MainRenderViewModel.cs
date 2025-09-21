using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Services.BlenderService;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Enums;
using SukiUI.Toasts;

namespace BlenderRenderQueue.ViewModels;

public partial class MainRenderViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _blenderPath = string.Empty;

    [ObservableProperty]
    private string _ffmpegPath = string.Empty;

    [ObservableProperty]
    private RenderQueueViewModel _renderQueue = new();

    [ObservableProperty]
    private bool _isBlenderPathValid;

    [ObservableProperty]
    private bool _isFFmpegPathValid;

    [ObservableProperty]
    private string _blenderVersion = string.Empty;

    [ObservableProperty]
    private string _blenderPlatform = string.Empty;

    [ObservableProperty]
    private string _blenderBranch = string.Empty;

    [ObservableProperty]
    private string _blenderHash = string.Empty;

    [ObservableProperty]
    private string _ffmpegVersion = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private bool _isLoadingBlenderInfo;

    [ObservableProperty]
    private bool _isLoadingFFmpegInfo;


    // 内部状态
    private BlenderExeService? _blenderService;
    private CancellationTokenSource? _versionCts;
    private SettingsViewModel? _settingsViewModel;

    // 视频生成进度 Toast 相关
    private ISukiToast? _videoGenerationToast;
    private ProgressBar? _videoGenerationProgressBar;

    public MainRenderViewModel()
    {
        // 订阅渲染队列事件
        RenderQueue.QueueStatusChanged += OnQueueStatusChanged;
        RenderQueue.TaskCompleted += OnTaskCompleted;
        RenderQueue.StatusMessageChanged += OnRenderQueueStatusMessageChanged;
        RenderQueue.ConfirmDialogRequested += OnConfirmDialogRequested;

        // 初始化设置并检测路径
        InitializeSettings();

        // 异步加载保存的数据
        _ = Task.Run(async () => await LoadSavedDataAsync());
    }

    partial void OnBlenderPathChanged(string value)
    {
        _versionCts?.Cancel();
        _versionCts = new CancellationTokenSource();
        var ct = _versionCts.Token;

        IsBlenderPathValid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);

        if (!IsBlenderPathValid)
        {
            ClearBlenderInfo();
            _blenderService = null;
            RenderQueue.SetBlenderService(null!);
            StatusMessage = "Blender路径无效";
            return;
        }

        // 异步获取Blender版本信息
        _ = Task.Run(async () => await LoadBlenderInfoAsync(value, ct));
    }

    partial void OnFfmpegPathChanged(string value)
    {
        IsFFmpegPathValid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);

        if (!IsFFmpegPathValid)
        {
            FfmpegVersion = string.Empty;
            StatusMessage = "FFmpeg路径无效";
            return;
        }

        // 异步获取FFmpeg版本信息
        _ = Task.Run(async () => await LoadFFmpegInfoAsync(value));

        // 设置FFmpeg路径到渲染队列
        RenderQueue.SetFFmpegPath(value);
    }

    private async Task LoadBlenderInfoAsync(string blenderPath, CancellationToken cancellationToken)
    {
        try
        {
            IsLoadingBlenderInfo = true;
            StatusMessage = "正在加载Blender信息...";

            var svc = new BlenderCliInfoService();
            var info = await svc.GetVersionInfoAsync(blenderPath, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            // 更新UI线程上的属性
            Dispatcher.UIThread.Post(() =>
            {
                BlenderVersion = info.Version;
                BlenderPlatform = info.Platform;
                BlenderBranch = info.Branch;
                BlenderHash = info.Hash;
                IsLoadingBlenderInfo = false;
                StatusMessage = $"Blender {info.Version} 已就绪";

                // 创建Blender服务并设置到渲染队列
                _blenderService = new BlenderExeService(blenderPath);
                RenderQueue.SetBlenderService(_blenderService);
            });
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingBlenderInfo = false;
                    StatusMessage = $"加载Blender信息失败: {ex.Message}";
                    ClearBlenderInfo();
                });
        }
    }

    private async Task LoadFFmpegInfoAsync(string ffmpegPath)
    {
        try
        {
            IsLoadingFFmpegInfo = true;
            StatusMessage = "正在加载FFmpeg信息...";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // 解析版本信息
                var lines = output.Split('\n');
                var versionLine = lines.FirstOrDefault(l => l.Contains("ffmpeg version"));
                if (!string.IsNullOrEmpty(versionLine))
                {
                    var version = versionLine.Split(' ')[2]; // 提取版本号
                    Dispatcher.UIThread.Post(() =>
                    {
                        FfmpegVersion = version;
                        IsLoadingFFmpegInfo = false;
                        StatusMessage = $"FFmpeg {version} 已就绪";
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoadingFFmpegInfo = false;
                StatusMessage = $"加载FFmpeg信息失败: {ex.Message}";
                FfmpegVersion = string.Empty;
            });
        }
    }

    private void ClearBlenderInfo()
    {
        BlenderVersion = string.Empty;
        BlenderPlatform = string.Empty;
        BlenderBranch = string.Empty;
        BlenderHash = string.Empty;
    }

    private void InitializeSettings()
    {
        _settingsViewModel = new SettingsViewModel();

        // 订阅设置变化事件
        _settingsViewModel.SettingsChanged += OnSettingsChanged;
        _settingsViewModel.InitializationCompleted += OnInitializationCompleted;

        // 开始初始化检测
        _settingsViewModel.StartInitialization();
    }

    private void OnInitializationCompleted(object? sender, InitializationCompletedEventArgs e)
    {
        // 如果检测失败，自动弹出设置对话框
        if (!e.IsBlenderDetected || !e.IsFFmpegDetected)
            ShowSettingsDialog();
        else
            // 检测成功，直接应用设置
            ApplySettings(_settingsViewModel!.BlenderPath, _settingsViewModel.FfmpegPath);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        ShowSettingsDialog();
    }

    private void ShowSettingsDialog()
    {
        // 确保设置ViewModel存在
        if (_settingsViewModel == null) InitializeSettings();

        // 使用 ToplevelService 获取顶层窗口的 DialogManager
        var dialogManager = GetDialogManager();
        if (dialogManager != null)
            dialogManager.CreateDialog()
                .WithTitle("设置")
                .WithContent(_settingsViewModel!)
                .WithActionButton("保存", _ => { _settingsViewModel!.SaveSettingsCommand.Execute(null); }, true)
                .WithActionButton("取消", _ => { }, true)
                .Dismiss().ByClickingBackground()
                .TryShow();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        ApplySettings(e.BlenderPath, e.FfmpegPath);
    }

    private void ApplySettings(string blenderPath, string ffmpegPath)
    {
        BlenderPath = blenderPath;
        FfmpegPath = ffmpegPath;
    }

    /// <summary>
    ///     通过 ToplevelService 获取顶层窗口的 DialogManager
    /// </summary>
    private ISukiDialogManager? GetDialogManager()
    {
        try
        {
            // 通过 ToplevelService 获取当前 ViewModel 对应的 Visual
            var visual = ToplevelService.GetVisualForContext(this);
            if (visual == null) return null;

            // 获取顶层窗口
            var topLevel = ToplevelService.GetTopLevelForContext(this);
            if (topLevel == null) return null;

            // 获取顶层窗口的 DataContext (应该是 MainWindowViewModel)
            if (topLevel.DataContext is MainWindowViewModel mainWindowViewModel)
                return mainWindowViewModel.DialogManager;

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] Error getting DialogManager: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     通过 ToplevelService 获取顶层窗口的 ToastManager
    /// </summary>
    private ISukiToastManager? GetToastManager()
    {
        try
        {
            // 通过 ToplevelService 获取当前 ViewModel 对应的 Visual
            var visual = ToplevelService.GetVisualForContext(this);
            if (visual == null) return null;

            // 获取顶层窗口
            var topLevel = ToplevelService.GetTopLevelForContext(this);
            if (topLevel == null) return null;

            // 获取顶层窗口的 DataContext (应该是 MainWindowViewModel)
            if (topLevel.DataContext is MainWindowViewModel mainWindowViewModel)
                return mainWindowViewModel.ToastManager;

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] Error getting ToastManager: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 显示 Toast 提示
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="content">内容</param>
    /// <param name="type">通知类型</param>
    private void ShowToast(string title, string content, NotificationType type)
    {
        try
        {
            var toastManager = GetToastManager();
            if (toastManager != null)
            {
                toastManager.CreateToast()
                    .WithTitle(title)
                    .WithContent(content)
                    .OfType(type)
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] Error showing toast: {ex.Message}");
        }
    }

    /// <summary>
    /// 显示视频生成成功 Toast，包含操作按钮
    /// </summary>
    /// <param name="statusMessage">状态消息，包含视频路径信息</param>
    private void ShowVideoGenerationSuccessToast(string statusMessage)
    {
        try
        {
            var toastManager = GetToastManager();
            if (toastManager != null)
            {
                // 从状态消息中提取视频路径
                var videoPath = ExtractVideoPathFromStatusMessage(statusMessage);

                toastManager.CreateToast()
                    .WithTitle("视频生成完成")
                    .WithContent("视频已成功生成！")
                    .OfType(NotificationType.Success)
                    .WithActionButton("关闭", _ => { }, true, SukiButtonStyles.Basic)
                    .WithActionButton("播放", _ => PlayVideo(videoPath), true, SukiButtonStyles.Standard)
                    .WithActionButton("打开位置", _ => OpenVideoLocation(videoPath), true)
                    .Queue();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] Error showing video generation success toast: {ex.Message}");
        }
    }

    /// <summary>
    /// 显示视频生成进度 Toast
    /// </summary>
    private void ShowVideoGenerationProgressToast()
    {
        try
        {
            var toastManager = GetToastManager();
            if (toastManager == null) return;
            // 创建进度条
            _videoGenerationProgressBar = new ProgressBar
            {
                Value = 0,
                ShowProgressText = true,
                Minimum = 0,
                Maximum = 100
            };

            // 创建进度 Toast
            _videoGenerationToast = toastManager.CreateToast()
                .WithTitle("正在生成视频...")
                .WithContent(_videoGenerationProgressBar)
                .OfType(NotificationType.Information)
                .Queue();

            // 订阅渲染队列的进度更新事件
            RenderQueue.PropertyChanged += OnRenderQueueProgressChanged;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] Error showing video generation progress toast: {ex.Message}");
        }
    }

    /// <summary>
    /// 监听渲染队列进度变化，更新 Toast 进度条
    /// </summary>
    private void OnRenderQueueProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RenderQueueViewModel.VideoGenerationProgress) &&
            _videoGenerationProgressBar != null)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                // FFmpegService 已经将帧数转换为百分比 (0-100)，直接使用
                _videoGenerationProgressBar.Value = RenderQueue.VideoGenerationProgress;
            });
        }
    }

    /// <summary>
    /// 关闭视频生成进度 Toast
    /// </summary>
    private void DismissVideoGenerationToast()
    {
        try
        {
            if (_videoGenerationToast != null)
            {
                var toastManager = GetToastManager();
                toastManager?.Dismiss(_videoGenerationToast);
                _videoGenerationToast = null;
            }

            if (_videoGenerationProgressBar != null)
            {
                _videoGenerationProgressBar = null;
            }

            // 取消订阅进度更新事件
            RenderQueue.PropertyChanged -= OnRenderQueueProgressChanged;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] Error dismissing video generation toast: {ex.Message}");
        }
    }

    /// <summary>
    /// 从状态消息中提取视频路径
    /// </summary>
    /// <param name="statusMessage">状态消息</param>
    /// <returns>视频路径</returns>
    private string ExtractVideoPathFromStatusMessage(string statusMessage)
    {
        try
        {
            // 状态消息格式通常是 "视频生成完成: C:\path\to\video.mp4"
            if (statusMessage.Contains("视频生成完成: "))
            {
                return statusMessage.Substring(statusMessage.IndexOf("视频生成完成: ") + "视频生成完成: ".Length);
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 播放视频
    /// </summary>
    /// <param name="videoPath">视频路径</param>
    private void PlayVideo(string videoPath)
    {
        var success = FileSystemHelper.PlayVideo(videoPath);
        if (!success)
        {
            ShowToast("播放失败", "无法播放视频文件", NotificationType.Error);
        }
    }

    /// <summary>
    /// 打开视频所在位置
    /// </summary>
    /// <param name="videoPath">视频路径</param>
    private void OpenVideoLocation(string videoPath)
    {
        try
        {
            if (string.IsNullOrEmpty(videoPath))
            {
                ShowToast("打开失败", "视频路径为空", NotificationType.Error);
                return;
            }

            var success = FileSystemHelper.OpenFileDirectory(videoPath);
            if (!success)
            {
                ShowToast("打开失败", "无法打开视频所在位置", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] ❌ Error opening video location: {ex.Message}");
            ShowToast("打开失败", $"无法打开位置: {ex.Message}", NotificationType.Error);
        }
    }

    private void OnConfirmDialogRequested(object? sender, ConfirmDialogRequestedEventArgs e)
    {
        // 使用 ToplevelService 获取顶层窗口的 DialogManager
        var dialogManager = GetDialogManager();
        if (dialogManager != null)
            dialogManager.CreateDialog()
                .WithTitle(e.Title)
                .WithContent(e.Content)
                .WithActionButton(e.CancelButtonText, _ => { }, true)
                .WithActionButton(e.ConfirmButtonText, _ => e.ConfirmAction(), true, "Flat", "Danger")
                .TryShow();
    }

    /// <summary>
    ///     加载保存的数据
    /// </summary>
    private async Task LoadSavedDataAsync()
    {
        try
        {
            Console.WriteLine("[MainRenderViewModel] Starting to load saved data...");

            // 等待设置初始化完成
            await Task.Delay(1000);

            // 加载设置
            if (_settingsViewModel != null) await _settingsViewModel.LoadSettingsFromFileAsync();

            // 等待BlenderService初始化完成后再加载队列数据
            Console.WriteLine("[MainRenderViewModel] Waiting for BlenderService initialization...");

            // 等待BlenderService初始化（最多等待10秒）
            var maxWaitTime = TimeSpan.FromSeconds(10);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWaitTime)
            {
                // 检查BlenderService是否已初始化
                if (RenderQueue.IsBlenderServiceReady())
                {
                    Console.WriteLine("[MainRenderViewModel] BlenderService is ready, loading queue data...");
                    await RenderQueue.LoadQueueDataAsync();
                    break;
                }

                await Task.Delay(500); // 每500ms检查一次
            }

            // 如果超时，仍然尝试加载队列数据（但可能没有BlenderService）
            if (DateTime.UtcNow - startTime >= maxWaitTime)
            {
                Console.WriteLine(
                    "[MainRenderViewModel] ⚠️ BlenderService initialization timeout, loading queue data anyway...");
                await RenderQueue.LoadQueueDataAsync();
            }

            Console.WriteLine("[MainRenderViewModel] ✅ Saved data loaded successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainRenderViewModel] ❌ Error loading saved data: {ex.Message}");
        }
    }


    private void OnQueueStatusChanged(object? sender, QueueStatusChangedEventArgs e)
    {
        StatusMessage = e.StatusMessage;

        // 检查是否是视频生成相关的状态消息，显示 Toast 提示
        if (e.StatusMessage.Contains("开始生成视频"))
        {
            ShowVideoGenerationProgressToast();
        }
        else if (e.StatusMessage.Contains("视频生成完成"))
        {
            DismissVideoGenerationToast();
            ShowVideoGenerationSuccessToast(e.StatusMessage);
        }
        else if (e.StatusMessage.Contains("视频生成失败") || e.StatusMessage.Contains("生成视频时出错"))
        {
            DismissVideoGenerationToast();
            ShowToast("视频生成失败", e.StatusMessage, NotificationType.Error);
        }
    }

    private void OnRenderQueueStatusMessageChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    private void OnTaskCompleted(object? sender, TaskCompletedEventArgs e)
    {
        var taskName = Path.GetFileName(e.Task.BlendFilePath);
        switch (e.Status)
        {
            case RenderTaskStatus.Completed:
                StatusMessage = $"任务完成: {taskName}";
                break;
            case RenderTaskStatus.Failed:
                StatusMessage = $"任务失败: {taskName}";
                break;
            case RenderTaskStatus.Cancelled:
                StatusMessage = $"任务取消: {taskName}";
                break;
        }
    }

    private void OnTaskStatusChanged(object? sender, RenderTaskStatusChangedEventArgs e)
    {
        // 可以在这里添加额外的任务状态处理逻辑
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        // 可以在这里添加额外的进度处理逻辑
    }


    public void Dispose()
    {
        _versionCts?.Cancel();
        _versionCts?.Dispose();

        RenderQueue.QueueStatusChanged -= OnQueueStatusChanged;
        RenderQueue.TaskCompleted -= OnTaskCompleted;
        RenderQueue.StatusMessageChanged -= OnRenderQueueStatusMessageChanged;
        RenderQueue.ConfirmDialogRequested -= OnConfirmDialogRequested;
        RenderQueue.PropertyChanged -= OnRenderQueueProgressChanged;

        if (_settingsViewModel != null)
        {
            _settingsViewModel.SettingsChanged -= OnSettingsChanged;
            _settingsViewModel.InitializationCompleted -= OnInitializationCompleted;
        }

        // 关闭视频生成进度 Toast
        DismissVideoGenerationToast();

        RenderQueue.Dispose();
        _blenderService?.Dispose();
    }
}