using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace BlenderRenderQueue.ViewModels;

public partial class MainRenderViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _blenderPath = string.Empty;


    [ObservableProperty]
    private RenderQueueViewModel _renderQueue;

    [ObservableProperty]
    private GlobalLogViewModel _globalLog;

    [ObservableProperty]
    private int _selectedNavigationIndex;

    [ObservableProperty]
    private bool _isBlenderPathValid;

    [ObservableProperty]
    private string _blenderValidationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasBlenderValidationError = false;

    [ObservableProperty]
    private string _blenderVersion = string.Empty;

    [ObservableProperty]
    private string _blenderPlatform = string.Empty;

    [ObservableProperty]
    private string _blenderBranch = string.Empty;

    [ObservableProperty]
    private string _blenderHash = string.Empty;


    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private bool _isLoadingBlenderInfo;

    [ObservableProperty]
    private SettingsViewModel? _settingsViewModel;

    // 内部状态
    private BlenderProcessService? _blenderProcessService;
    private readonly IRenderLogService _logService;
    private CancellationTokenSource? _versionCts;
    public Task InitialLoadTask { get; }


    public MainRenderViewModel(
        SettingsViewModel settingsViewModel,
        RenderQueueViewModel renderQueue,
        GlobalLogViewModel globalLog,
        IRenderLogService logService)
    {
        _logService = logService;
        RenderQueue = renderQueue;
        GlobalLog = globalLog;
        GlobalLog.TaskNavigationRequested += OnGlobalLogTaskNavigationRequested;

        // 订阅渲染队列事件
        RenderQueue.QueueStatusChanged += OnQueueStatusChanged;
        RenderQueue.TaskCompleted += OnTaskCompleted;
        RenderQueue.StatusMessageChanged += OnRenderQueueStatusMessageChanged;
        RenderQueue.ConfirmDialogRequested += OnConfirmDialogRequested;
        RenderQueue.PropertyChanged += OnRenderQueuePropertyChanged;

        // 初始化设置并检测路径
        InitializeSettings(settingsViewModel);

        // 异步加载保存的数据
        InitialLoadTask = Task.Run(LoadSavedDataAsync);
    }

    private void ValidateSelectedBlender()
    {
        var settings = SettingsViewModel;
        if (settings == null)
        {
            return;
        }

        _versionCts?.Cancel();
        _versionCts = new CancellationTokenSource();
        var ct = _versionCts.Token;

        // 重置验证状态
        HasBlenderValidationError = false;
        BlenderValidationMessage = string.Empty;

        var selectedBlender = settings.SelectedBlenderExecutable;
        if (selectedBlender == null)
        {
            IsBlenderPathValid = false;
            HasBlenderValidationError = true;
            BlenderValidationMessage = "Blender_SelectExecutable";
            ClearBlenderInfo();
            CleanupBlenderService();
            StatusMessage = "Blender_PathInvalid";
            _logService.Write(RenderLogLevel.Warning, RenderLogScope.System, "未选择 Blender 可执行文件。", source: nameof(MainRenderViewModel));
            return;
        }

        if (!File.Exists(selectedBlender.Path))
        {
            IsBlenderPathValid = false;
            HasBlenderValidationError = true;
            BlenderValidationMessage = "指定的文件不存在";
            ClearBlenderInfo();
            CleanupBlenderService();
            StatusMessage = "Blender_PathInvalid";
            _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"Blender 路径不存在: {selectedBlender.Path}", source: nameof(MainRenderViewModel));
            return;
        }

        // 异步获取Blender版本信息
        _ = Task.Run(async () => await LoadBlenderInfoAsync(selectedBlender, ct));
    }


    private async Task LoadBlenderInfoAsync(BlenderExecutable blenderExecutable, CancellationToken cancellationToken)
    {
        try
        {
            IsLoadingBlenderInfo = true;
            StatusMessage = "正在加载Blender信息...";
            _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"开始验证 Blender: {blenderExecutable.Path}", source: nameof(MainRenderViewModel));

            var svc = new BlenderCliInfoService();
            var info = await svc.GetVersionInfoAsync(blenderExecutable.Path, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            // 更新UI线程上的属性
            Dispatcher.UIThread.Post(() =>
            {
                BlenderVersion = info.Version ?? string.Empty;
                BlenderPlatform = info.Platform ?? string.Empty;
                BlenderBranch = info.Branch ?? string.Empty;
                BlenderHash = info.Hash ?? string.Empty;
                IsLoadingBlenderInfo = false;
                IsBlenderPathValid = true;
                HasBlenderValidationError = false;
                BlenderValidationMessage = string.Empty;
                StatusMessage = $"Blender {info.Version} 已就绪";

                // 先释放旧的Blender进程服务（如果存在）
                if (_blenderProcessService != null)
                {
                    Console.WriteLine(
                        $"[MainRenderViewModel] Disposing old Blender process service");
                    _blenderProcessService.Dispose();
                }

                // 设置Blender路径到渲染队列（不创建长期运行的服务）
                Console.WriteLine($"[MainRenderViewModel] Setting Blender path: {blenderExecutable.Path}");
                RenderQueue.SetBlenderPath(blenderExecutable.Path);

                // 只有在验证成功时才创建临时服务用于视频生成
                try
                {
                    _blenderProcessService = new BlenderProcessService(blenderExecutable.Path);
                    Console.WriteLine(
                        $"[MainRenderViewModel] Temporary Blender process service created for video generation");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MainRenderViewModel] Failed to create Blender process service: {ex.Message}");
                    // 即使服务创建失败，我们仍然认为Blender路径是有效的，因为版本信息获取成功了
                    _blenderProcessService = null;
                }
            });

            _logService.Write(
                RenderLogLevel.Info,
                RenderLogScope.System,
                $"Blender {info.Version} 已就绪",
                source: nameof(MainRenderViewModel),
                metadata: new Dictionary<string, string>
                {
                    ["path"] = blenderExecutable.Path,
                    ["platform"] = info.Platform ?? string.Empty,
                    ["branch"] = info.Branch ?? string.Empty
                });
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"Blender 验证失败: {ex.Message}", source: nameof(MainRenderViewModel));
                Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingBlenderInfo = false;
                    IsBlenderPathValid = false;
                    HasBlenderValidationError = true;
                    BlenderValidationMessage = $"Blender验证失败: {ex.Message}";
                    StatusMessage = $"加载Blender信息失败: {ex.Message}";
                    ClearBlenderInfo();
                    CleanupBlenderService();
                });
            }
        }
    }


    private void ClearBlenderInfo()
    {
        BlenderVersion = string.Empty;
        BlenderPlatform = string.Empty;
        BlenderBranch = string.Empty;
        BlenderHash = string.Empty;
    }

    private void CleanupBlenderService()
    {
        // 正确释放旧的Blender进程服务
        if (_blenderProcessService != null)
        {
            Console.WriteLine(
                $"[MainRenderViewModel] Disposing Blender process service due to invalid path");
            _blenderProcessService.Dispose();
        }

        _blenderProcessService = null;
        RenderQueue.SetBlenderPath(string.Empty);
    }

    private void InitializeSettings(SettingsViewModel settingsViewModel)
    {
        SettingsViewModel = settingsViewModel;
        var settings = SettingsViewModel;

        // 订阅设置变化事件
        settings.SettingsChanged += OnSettingsChanged;
        settings.InitializationCompleted += OnInitializationCompleted;
        settings.BlenderValidationChanged += OnBlenderValidationChanged;

        // 开始初始化检测（这会自动加载设置）
        settings.StartInitialization();
    }

    private void OnInitializationCompleted(object? sender, InitializationCompletedEventArgs e)
    {
        var settings = SettingsViewModel;
        if (settings == null)
        {
            return;
        }

        // 检测完成后，直接应用设置（不再自动弹出对话框，用户可以通过侧边菜单访问设置）
        var selectedPath = settings.SelectedBlenderExecutable?.Path ?? string.Empty;
        ApplySettings(selectedPath, settings.DefaultRenderTimeoutSeconds,
            settings.MaxRetryAttempts, settings.VideoCodec.Value,
            settings.VideoQuality.Value);
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        // 在导航到设置页面时，同步队列状态（与开始队列按钮逻辑保持一致）
        if (SettingsViewModel != null)
        {
            SettingsViewModel.UpdateQueueState(RenderQueue.QueueState);
        }
        

    }

    private void ShowSettingsDialog()
    {
        // 使用 ToplevelService 获取顶层窗口的 DialogManager
        var dialogManager = GetDialogManager();
        if (dialogManager != null)
        {
            var settings = SettingsViewModel;
            if (settings == null)
            {
                return;
            }

            dialogManager.CreateDialog()
                .WithTitle(Localizer.Localizer.Instance["Settings"])
                .WithContent(settings)
                .WithActionButton(Localizer.Localizer.Instance["Save"], async _ => { await settings.SaveSettingsToFileAsync(); }, true)
                .WithActionButton(Localizer.Localizer.Instance["Cancel"], _ => { }, true)
                .Dismiss().ByClickingBackground()
                .TryShow();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 只更新非Blender相关的设置，不重新验证Blender
        RenderQueue.SetGlobalRenderTimeout(e.DefaultRenderTimeoutSeconds);
        RenderQueue.SetGlobalMaxRetryAttempts(e.MaxRetryAttempts);
        
        // 更新视频生成设置
        RenderQueue.SetVideoCodec(e.VideoCodec);
        RenderQueue.SetVideoQuality(e.VideoQuality);
    }

    private void OnBlenderValidationChanged(object? sender, BlenderValidationChangedEventArgs e)
    {
        // 同步验证状态到主界面
        IsBlenderPathValid = e.IsValid;
        HasBlenderValidationError = !e.IsValid;
        BlenderValidationMessage = e.Message;
        
        Console.WriteLine($"[MainRenderViewModel] OnBlenderValidationChanged - IsValid: {e.IsValid}, Message: {e.Message}");
        
        if (e.IsValid)
        {
            // 如果验证成功，检查是否有正在运行的任务
            var hasRunningTasks = RenderQueue.HasRunningTasks;
            var activeTaskCount = RenderQueue.ActiveTaskCount;
            var queueState = RenderQueue.QueueState;
            
            Console.WriteLine($"[MainRenderViewModel] 检查运行任务 - HasRunningTasks: {hasRunningTasks}, ActiveTaskCount: {activeTaskCount}, QueueState: {queueState}");
            
            if (hasRunningTasks)
            {
                // 如果有正在运行的任务，显示警告并询问用户
                Console.WriteLine("[MainRenderViewModel] 检测到运行任务，显示警告弹窗");
                ShowBlenderSwitchWarning();
            }
            else
            {
                // 如果没有正在运行的任务，安全地切换Blender服务
                Console.WriteLine("[MainRenderViewModel] 没有运行任务，直接切换Blender");
                var selectedBlender = SettingsViewModel?.SelectedBlenderExecutable;
                if (selectedBlender != null)
                {
                    _ = Task.Run(async () => await LoadBlenderInfoAsync(selectedBlender, CancellationToken.None));
                }
            }
        }
        else
        {
            // 如果验证失败，清理Blender服务
            StatusMessage = $"Blender验证失败: {e.Message}";
            CleanupBlenderService();
        }
    }

    private void ShowBlenderSwitchWarning()
    {
        Console.WriteLine("[MainRenderViewModel] ShowBlenderSwitchWarning 被调用");
        
        var selectedBlender = SettingsViewModel?.SelectedBlenderExecutable;
        if (selectedBlender == null) 
        {
            Console.WriteLine("[MainRenderViewModel] selectedBlender 为 null，退出");
            return;
        }

        var blenderName = selectedBlender.VersionBranchDisplay;
        Console.WriteLine($"[MainRenderViewModel] 准备显示警告弹窗，Blender: {blenderName}");
        
        // 确保在UI线程上执行
        Dispatcher.UIThread.Post(() =>
        {
            Console.WriteLine("[MainRenderViewModel] 在UI线程上执行警告弹窗");
            
            var dialogManager = GetDialogManager();
            if (dialogManager == null)
            {
                Console.WriteLine("[MainRenderViewModel] 无法获取DialogManager，跳过警告弹窗");
                // 如果无法获取DialogManager，直接执行切换
                Task.Run(async () => await LoadBlenderInfoAsync(selectedBlender, CancellationToken.None));
                return;
            }
            
            Console.WriteLine("[MainRenderViewModel] 创建并显示警告弹窗");
            dialogManager.CreateDialog()
                .WithTitle("⚠️ 切换Blender警告")
                .WithContent($"检测到有正在运行的渲染任务。\n\n切换到 {blenderName} 将会中断当前正在进行的任务。\n\n是否确定要切换？")
                .WithActionButton("取消", _ => 
                {
                    // 取消切换，恢复之前的选择
                    Console.WriteLine("[MainRenderViewModel] 用户取消了Blender切换");
                    StatusMessage = "Blender切换已取消";
                }, true)
                .WithActionButton("确定切换", _ => 
                {
                    // 用户确认切换，执行切换
                    Console.WriteLine($"[MainRenderViewModel] 用户确认切换Blender到: {selectedBlender.Path}");
                    Task.Run(async () => await LoadBlenderInfoAsync(selectedBlender, CancellationToken.None));
                })
                .Dismiss().ByClickingBackground()
                .TryShow();
        });
    }

    private void ApplySettings(string blenderPath, int defaultRenderTimeoutSeconds, int maxRetryAttempts,
        string videoCodec, string videoQuality)
    {
        // 验证选中的Blender
        ValidateSelectedBlender();

        // 更新全局超时设置和重试次数
        RenderQueue.SetGlobalRenderTimeout(defaultRenderTimeoutSeconds);
        RenderQueue.SetGlobalMaxRetryAttempts(maxRetryAttempts);

        // 更新视频生成设置
        RenderQueue.SetVideoCodec(videoCodec);
        RenderQueue.SetVideoQuality(videoQuality);
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




    private void OnRenderQueuePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 监听QueueState变化，通知SettingsViewModel（与开始队列按钮逻辑保持一致）
        if (e.PropertyName == nameof(RenderQueue.QueueState))
        {
            var queueState = RenderQueue.QueueState;
            Console.WriteLine($"[MainRenderViewModel] 队列状态变化 - QueueState: {queueState}");
            
            // 通知SettingsViewModel更新CanSwitchBlender状态
            SettingsViewModel?.UpdateQueueState(queueState);
        }
    }


    /// <summary>
    /// 从状态消息中提取视频路径
    /// </summary>
    /// <param name="statusMessage">状态消息</param>
    /// <returns>视频路径</returns>

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

            // 等待BlenderService初始化完成后再加载队列数据
            Console.WriteLine("[MainRenderViewModel] Waiting for Blender initialization...");

            // 等待BlenderService初始化（最多等待10秒）
            var maxWaitTime = TimeSpan.FromSeconds(10);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWaitTime)
            {
                // 检查BlenderService是否已初始化
                if (RenderQueue.IsBlenderServiceReady())
                {
                    Console.WriteLine("[MainRenderViewModel] Blender is ready, loading queue data...");
                    await RenderQueue.LoadQueueDataAsync();
                    break;
                }

                await Task.Delay(500); // 每500ms检查一次
            }

            // 如果超时，仍然尝试加载队列数据（但可能没有BlenderService）
            if (DateTime.UtcNow - startTime >= maxWaitTime)
            {
                Console.WriteLine(
                    "[MainRenderViewModel] ⚠️ Blender initialization timeout, loading queue data anyway...");
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
        // 视频生成相关的状态消息现在在任务级别处理，这里不再需要特殊处理
        {
            try
            {
                StatusMessage = Localizer.Localizer.Instance[e.StatusMessage];
            }
            catch
            {
                StatusMessage = e.StatusMessage;
            }
        }
    }

    private void OnRenderQueueStatusMessageChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    private void OnGlobalLogTaskNavigationRequested(object? sender, Guid taskId)
    {
        if (!RenderQueue.SelectTask(taskId))
        {
            return;
        }

        SelectedNavigationIndex = 0;
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
        RenderQueue.PropertyChanged -= OnRenderQueuePropertyChanged;
        GlobalLog.TaskNavigationRequested -= OnGlobalLogTaskNavigationRequested;
        GlobalLog.Dispose();

        if (SettingsViewModel != null)
        {
            SettingsViewModel.SettingsChanged -= OnSettingsChanged;
            SettingsViewModel.InitializationCompleted -= OnInitializationCompleted;
            SettingsViewModel.BlenderValidationChanged -= OnBlenderValidationChanged;
        }


        RenderQueue.Dispose();
        _blenderProcessService?.Dispose();
    }
}
