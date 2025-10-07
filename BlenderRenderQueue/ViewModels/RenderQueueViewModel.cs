using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Localizer;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlenderRenderQueue.ViewModels;

/// <summary>
/// 队列渲染结束后的行为选项
/// </summary>
public enum PostRenderBehavior
{
    None,
    Shutdown,
    Restart
}

public partial class RenderQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<RenderTaskViewModel> _renderTasks = [];

    [ObservableProperty]
    private RenderTaskViewModel? _selectedTask;

    [ObservableProperty]
    private RenderTaskViewModel? _currentRenderingTask;

    [ObservableProperty]
    private QueueState _queueState = QueueState.Idle;

    [ObservableProperty]
    private int _activeTaskCount;

    [ObservableProperty]
    private int _completedTaskCount;

    [ObservableProperty]
    private int _failedTaskCount;

    [ObservableProperty]
    private string _queueStatusText = "Queue_Idle";

    [ObservableProperty]
    private bool _autoStartNext = true; // 自动开始下一个任务

    [ObservableProperty]
    private PostRenderBehavior _postRenderBehavior = PostRenderBehavior.None;

    /// <summary>
    /// 后渲染行为显示文字
    /// </summary>
    public string PostRenderBehaviorText
    {
        get
        {
            var prefix = Localizer.Localizer.Instance["SystemControl_PostRenderBehavior"]; // "渲染完成后"
            var action = PostRenderBehavior switch
            {
                PostRenderBehavior.None => Localizer.Localizer.Instance["SystemControl_None"],
                PostRenderBehavior.Shutdown => Localizer.Localizer.Instance["SystemControl_Shutdown"],
                PostRenderBehavior.Restart => Localizer.Localizer.Instance["SystemControl_Restart"],
                _ => Localizer.Localizer.Instance["SystemControl_None"]
            };
            return $"{prefix}: {action}";
        }
    }

    /// <summary>
    /// 后渲染行为图标颜色
    /// </summary>
    public Avalonia.Media.IBrush PostRenderBehaviorIconColor
    {
        get
        {
            return PostRenderBehavior switch
            {
                PostRenderBehavior.None => GetResourceBrush("SukiTextColor") ?? Avalonia.Media.Brushes.Gray,
                PostRenderBehavior.Shutdown => GetResourceBrush("SukiDangerColor") ?? Avalonia.Media.Brushes.Red,
                PostRenderBehavior.Restart => GetResourceBrush("SukiWarningColor") ?? Avalonia.Media.Brushes.Orange,
                _ => GetResourceBrush("SukiTextColor") ?? Avalonia.Media.Brushes.Gray
            };
        }
    }

    /// <summary>
    /// 获取资源画笔
    /// </summary>
    private Avalonia.Media.IBrush? GetResourceBrush(string resourceKey)
    {
        if (Avalonia.Application.Current?.TryGetResource(resourceKey, Avalonia.Styling.ThemeVariant.Default, out var resource) == true)
        {
            return resource as Avalonia.Media.IBrush;
        }
        return null;
    } // 队列渲染结束后的行为

    
    // 硬件监控现在由HardwareChartView直接管理，不再需要在这里维护
    
    // 暂停/恢复相关

    // 暂停状态记录
    private RenderTaskViewModel? _pausedTask; // 暂停时的任务
    private int _pausedFrame; // 暂停时的帧号


    // 剩余时间计算相关
    private readonly Queue<TimeSpan> _recentFrameRenderTimes = new(); // 最近帧渲染时间队列
    private const int MaxRecentFrames = 3; // 最多记录3帧
    private System.Timers.Timer? _remainingTimeTimer; // 剩余时间更新定时器

    [ObservableProperty]
    private string _remainingTimeText = string.Empty; // 剩余时间文本

    // 文件监控相关
    private FileSystemWatcher? _blenderDataWatcher; // 监控Blender插件写入的文件

    // AOT兼容的JSON序列化选项
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    // 计算属性 - 用于UI绑定
    public bool IsQueueRunning => QueueState == QueueState.Running;
    public bool IsQueueActive => QueueState == QueueState.Running || QueueState == QueueState.Paused;
    public bool HasNoTasks => RenderTasks.Count == 0;
    public bool HasRunningTasks => ActiveTaskCount > 0;

    // 帧数相关的计算属性 - 只计算启用且有效的任务，使用实际渲染用的帧范围
    public int TotalFrames => RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.RealTotalFrames);

    public int CompletedFrames => RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t =>
    {
        var totalFrames = Math.Max(0, t.RealTotalFrames);
        return (int)(totalFrames * t.OverallProgress01);
    });

    // 队列进度直接计算，不需要事件更新 - 只计算启用的任务
    public double OverallQueueProgress =>
        RenderTasks.Any(t => t.Enable) && TotalFrames > 0 ? (double)CompletedFrames / TotalFrames : 0.0;

    public int OverallQueueProgressInt => (int)(OverallQueueProgress * 100);


    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        // 统一使用 hh:mm:ss 格式
        return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }

    /// <summary>
    ///     记录帧渲染时间，用于计算剩余时间
    /// </summary>
    /// <param name="frameNumber">完成的帧号</param>
    /// <param name="frameRenderTime">帧渲染时间</param>
    private void RecordFrameCompletion(int frameNumber, TimeSpan frameRenderTime)
    {
        // 只记录有效的渲染时间
        if (frameRenderTime.TotalSeconds > 0)
        {
            _recentFrameRenderTimes.Enqueue(frameRenderTime);

            // 保持队列大小不超过最大帧数
            while (_recentFrameRenderTimes.Count > MaxRecentFrames) _recentFrameRenderTimes.Dequeue();

            Console.WriteLine(
                $"[RecordFrameCompletion] Frame {frameNumber} completed, render time: {frameRenderTime.TotalSeconds:F2}s");
        }
    }

    /// <summary>
    ///     定时器事件处理，更新剩余时间
    /// </summary>
    private void OnRemainingTimeTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // 在UI线程上更新
        Dispatcher.UIThread.Post(() => { UpdateRemainingTime(); });
    }

    /// <summary>
    ///     更新剩余时间文本
    /// </summary>
    private void UpdateRemainingTime()
    {
        if (!IsQueueRunning)
        {
            RemainingTimeText = string.Empty;
            return;
        }

        // 计算整个队列的剩余帧数
        var remainingFrames = TotalFrames - CompletedFrames;
        if (remainingFrames <= 0)
        {
            RemainingTimeText = string.Empty;
            return;
        }

        // 如果没有帧渲染时间数据，显示"计算中..."
        if (_recentFrameRenderTimes.Count == 0)
        {
            RemainingTimeText = "Queue_Calculating";
            return;
        }

        // 计算平均每帧渲染时间
        var averageRenderTime = _recentFrameRenderTimes.Average(rt => rt.TotalSeconds);
        var estimatedRemainingSeconds = remainingFrames * averageRenderTime;

        // 显示计算出的剩余时间
        var formattedTime = FormatTimeSpan(TimeSpan.FromSeconds(estimatedRemainingSeconds));
        RemainingTimeText = $"Queue_RemainingTimeFormat:{formattedTime}";

        Console.WriteLine(
            $"[RemainingTime] RemainingFrames: {remainingFrames}, AvgRenderTime: {averageRenderTime:F2}s, Estimated: {estimatedRemainingSeconds:F2}s, Display: {formattedTime}");
    }


    public bool CanStartQueue
    {
        get
        {
            // 没有任务时不可见（通过HasNoTasks控制）
            if (HasNoTasks) return false;

            // 有任务时，根据队列状态和可用任务数量决定是否可用
            var hasAvailableTasks = RenderTasks.Any(t => t.Enable && t.IsValid);
            var canStart = (QueueState == QueueState.Idle || QueueState == QueueState.Completed) && hasAvailableTasks;

            return canStart;
        }
    }

    public bool CanShowStartQueue
    {
        get
        {
            // 没有任务时不可见
            if (HasNoTasks) return false;

            // 有任务时，只有在队列空闲或完成时才显示开始按钮
            // 队列运行/暂停时显示其他控制按钮（暂停、恢复、停止）
            return QueueState == QueueState.Idle || QueueState == QueueState.Completed;
        }
    }

    public bool CanStopQueue => QueueState == QueueState.Running;

    public bool CanPauseQueue => QueueState == QueueState.Running && ActiveTaskCount > 0;

    public bool CanResumeQueue => QueueState == QueueState.Paused;

    /// <summary>
    /// 获取当前后渲染行为的图标
    /// </summary>
    public string PostRenderBehaviorIcon
    {
        get
        {
            return PostRenderBehavior switch
            {
                PostRenderBehavior.None => "ArrowRight",
                PostRenderBehavior.Shutdown => "Power",
                PostRenderBehavior.Restart => "Restart",
                _ => "ArrowRight"
            };
        }
    }

    /// <summary>
    ///     设置全局渲染超时时间
    /// </summary>
    /// <param name="timeoutSeconds">超时时间（秒）</param>
    public void SetGlobalRenderTimeout(int timeoutSeconds)
    {
        _globalRenderTimeoutSeconds = timeoutSeconds;

        // 更新所有现有任务的超时设置
        foreach (var task in RenderTasks) task.SetGlobalRenderTimeout(timeoutSeconds);
    }

    /// <summary>
    ///     设置全局最大重试次数
    /// </summary>
    /// <param name="maxRetryAttempts">最大重试次数</param>
    public void SetGlobalMaxRetryAttempts(int maxRetryAttempts)
    {
        _globalMaxRetryAttempts = maxRetryAttempts;

        // 更新所有现有任务的重试次数设置
        foreach (var task in RenderTasks) task.SetGlobalMaxRetryAttempts(maxRetryAttempts);
    }


    public void SetVideoCodec(string codec)
    {
        _videoCodec = codec;
    }

    public void SetVideoQuality(string quality)
    {
        _videoQuality = quality;
    }

    public bool CanModifyTasks => QueueState is QueueState.Running or QueueState.Paused;

    // 内部状态
    private readonly List<Task> _runningTasks = new();
    private BlenderProcessService? _blenderProcessService;
    private BlenderVideoService? _blenderVideoService;
    private BlenderProcessService? _processService; // 新的进程管理服务
    private string? _blenderPath; // 存储Blender路径，不创建长期运行的服务
    private int _globalRenderTimeoutSeconds = 300; // 默认5分钟
    private int _globalMaxRetryAttempts = 3; // 默认最大重试3次
    private string _videoCodec = "H264"; // 默认使用H264编码
    private string _videoQuality = "PERC_LOSSLESS"; // 默认感知无损质量
    private readonly IDataPersistenceService _dataPersistenceService = new DataPersistenceService();
    private readonly object _queueLock = new();

    // 事件
    public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<ConfirmDialogRequestedEventArgs>? ConfirmDialogRequested;

    public RenderQueueViewModel()
    {
        // 初始化剩余时间更新定时器
        _remainingTimeTimer = new System.Timers.Timer(1000); // 每秒更新一次
        _remainingTimeTimer.Elapsed += OnRemainingTimeTimerElapsed;
        _remainingTimeTimer.AutoReset = true;

        // 初始化文件监控
        InitializeBlenderDataWatcher();

        // 监听任务状态变化
        RenderTasks.CollectionChanged += (s, e) =>
        {
            UpdateQueueStatistics();
            // 任务集合变化时自动保存
            AutoSaveQueueData();

            // 通知按钮状态属性变更
            OnPropertyChanged(nameof(CanStartQueue));
            OnPropertyChanged(nameof(CanShowStartQueue));
        };

        // 监听队列状态变化，通知计算属性更新
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(QueueState) || e.PropertyName == nameof(ActiveTaskCount) ||
                e.PropertyName == nameof(RenderTasks))
            {
                OnPropertyChanged(nameof(IsQueueRunning));
                OnPropertyChanged(nameof(IsQueueActive));
                OnPropertyChanged(nameof(HasNoTasks));
                OnPropertyChanged(nameof(HasRunningTasks));
                OnPropertyChanged(nameof(TotalFrames));
                OnPropertyChanged(nameof(CompletedFrames));
                OnPropertyChanged(nameof(CanStartQueue));
                OnPropertyChanged(nameof(CanShowStartQueue));
                OnPropertyChanged(nameof(CanStopQueue));
                OnPropertyChanged(nameof(CanPauseQueue));
                OnPropertyChanged(nameof(CanResumeQueue));
                OnPropertyChanged(nameof(CanModifyTasks));
            }
            
            if (e.PropertyName == nameof(PostRenderBehavior))
            {
                OnPropertyChanged(nameof(PostRenderBehaviorIcon));
                OnPropertyChanged(nameof(PostRenderBehaviorText));
                OnPropertyChanged(nameof(PostRenderBehaviorIconColor));
            }
        };

//         // Debug 模式下添加测试任务
// #if DEBUG
//         AddTestTaskIfExists();
// #endif
    }

    [RelayCommand]
    private async Task AddTask()
    {
        if (!IsBlenderServiceReady())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPathRequired"]);
            return;
        }

        var blendFile = await SelectBlendFile();
        if (string.IsNullOrWhiteSpace(blendFile)) return;

        AddTaskToQueue(blendFile);
    }

    [RelayCommand]
    private async Task AddMultipleTasks()
    {
        if (!IsBlenderServiceReady())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPathRequired"]);
            return;
        }

        var blendFiles = await SelectMultipleBlendFiles();
        if (blendFiles == null || !blendFiles.Any()) return;

        foreach (var blendFile in blendFiles) AddTaskToQueue(blendFile);

        Console.WriteLine($"[DEBUG] AddMultipleTasks completed - Total tasks: {RenderTasks.Count}");
    }

    [RelayCommand]
    private void AddDroppedFiles(IEnumerable<IStorageItem> files)
    {
        if (!IsBlenderServiceReady())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPathRequired"]);
            return;
        }

        var blendFiles = files
            .OfType<IStorageFile>()
            .Where(file => file.Name.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!blendFiles.Any())
        {
            StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_DragBlendFiles"]);
            return;
        }

        foreach (var file in blendFiles)
        {
            var filePath = file.Path.LocalPath;
            AddTaskToQueue(filePath);
        }

        StatusMessageChanged?.Invoke(this,
            string.Format(Localizer.Localizer.Instance["Toast_TasksAddedSuccessfully"], blendFiles.Count));
    }

    private void AddTaskToQueue(string blendFilePath)
    {
        try
        {
            // 新任务默认不覆写帧范围，使用场景默认值
            var task = new RenderTaskViewModel(blendFilePath, 1, 1);

            // 设置全局超时和重试次数
            task.SetGlobalRenderTimeout(_globalRenderTimeoutSeconds);
            task.SetGlobalMaxRetryAttempts(_globalMaxRetryAttempts);

            // 设置视频生成相关参数
            task.SetVideoCodec(_videoCodec);
            task.SetVideoQuality(_videoQuality);
            task.SetProcessService(_processService);

            // 先添加到队列，显示加载状态
            RenderTasks.Add(task);

            // 订阅任务事件
            SubscribeToTaskEvents(task);

            // 设置队列运行状态，影响CanRefresh属性
            task.SetQueueRunningState(QueueState == QueueState.Running);

            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskAdded"], Path.GetFileName(blendFilePath)));

            // 异步加载文件属性，不阻塞UI
            if (IsBlenderServiceReady())
            {
                Console.WriteLine(
                    $"[RenderQueueViewModel] Starting async file properties loading for: {Path.GetFileName(blendFilePath)}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await task.LoadFilePropertiesAsync(_blenderPath!);
                        Console.WriteLine(
                            $"[RenderQueueViewModel] ✅ File properties loaded: {Path.GetFileName(blendFilePath)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[RenderQueueViewModel] ❌ Failed to load file properties for {Path.GetFileName(blendFilePath)}: {ex.Message}");
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

    [RelayCommand]
    private void RemoveSelectedTask()
    {
        if (SelectedTask == null) return;

        // 保存对选中任务的引用，避免在操作过程中被意外清空
        var taskToRemove = SelectedTask;

        // 如果任务正在运行，先停止
        if (taskToRemove.Status == RenderTaskStatus.Running) taskToRemove.StopRender();

        // 取消订阅事件
        UnsubscribeFromTaskEvents(taskToRemove);

        // 从集合中移除任务
        RenderTasks.Remove(taskToRemove);

        // 释放任务资源
        taskToRemove.Dispose();

        // 清空选中任务
        SelectedTask = null;

        UpdateQueueStatistics();
    }

    [RelayCommand]
    private void RemoveTask(RenderTaskViewModel? taskToRemove)
    {
        if (taskToRemove == null) return;

        // 如果任务已经处于预备删除状态，则真正删除
        if (taskToRemove.IsPendingDeletion)
        {
            // 如果任务正在运行，先停止
            if (taskToRemove.Status == RenderTaskStatus.Running) taskToRemove.StopRender();

            // 如果删除的是当前选中的任务，记录位置以便选择新任务
            var wasSelected = SelectedTask == taskToRemove;
            var selectedIndex = wasSelected ? RenderTasks.IndexOf(taskToRemove) : -1;

            // 取消订阅事件
            UnsubscribeFromTaskEvents(taskToRemove);

            // 从集合中移除任务
            RenderTasks.Remove(taskToRemove);

            // 释放任务资源
            taskToRemove.Dispose();

            // 如果删除的是当前选中的任务，选择最近的其他任务
            if (wasSelected)
            {
                if (RenderTasks.Count > 0)
                {
                    if (selectedIndex < RenderTasks.Count)
                        // 选择原来位置的任务（现在是下一个任务）
                        SelectedTask = RenderTasks[selectedIndex];
                    else if (selectedIndex > 0)
                        // 选择上一个任务
                        SelectedTask = RenderTasks[selectedIndex - 1];
                    else
                        // 选择第一个任务
                        SelectedTask = RenderTasks[0];
                }
                else
                {
                    // 没有其他任务，清空选中
                    SelectedTask = null;
                }
            }

            UpdateQueueStatistics();
        }
        else
        {
            // 第一次点击：设置预备删除状态
            // 先清除其他任务的预备删除状态
            ClearPendingDeletionStates();

            // 设置当前任务为预备删除状态
            taskToRemove.IsPendingDeletion = true;
        }
    }

    /// <summary>
    ///     清除所有任务的预备删除状态
    /// </summary>
    private void ClearPendingDeletionStates()
    {
        foreach (var task in RenderTasks) task.IsPendingDeletion = false;
    }

    [RelayCommand]
    private void RemoveAllTasks()
    {
        // 请求显示确认对话框
        ConfirmDialogRequested?.Invoke(this, new ConfirmDialogRequestedEventArgs(
            Localizer.Localizer.Instance["ConfirmClearAll_Title"],
            string.Format(Localizer.Localizer.Instance["ConfirmClearAll_Message"], RenderTasks.Count),
            Localizer.Localizer.Instance["ConfirmClearAll_Cancel"],
            Localizer.Localizer.Instance["ConfirmClearAll_Confirm"],
            ExecuteRemoveAllTasks));
    }

    private void ExecuteRemoveAllTasks()
    {
        // 停止所有运行中的任务
        foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running)) task.StopRender();

        // 取消订阅所有事件并释放资源
        foreach (var task in RenderTasks)
        {
            UnsubscribeFromTaskEvents(task);
            task.Dispose();
        }

        RenderTasks.Clear();
        SelectedTask = null;
        UpdateQueueStatistics();

        StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_AllTasksCleared"]);
    }

    [RelayCommand]
    private void RemoveCompletedTasks()
    {
        var completedTasks = RenderTasks.Where(t =>
            t.Status == RenderTaskStatus.Completed ||
            t.Status == RenderTaskStatus.Failed ||
            t.Status == RenderTaskStatus.Cancelled).ToList();

        foreach (var task in completedTasks)
        {
            UnsubscribeFromTaskEvents(task);
            RenderTasks.Remove(task);
            task.Dispose();
        }

        UpdateQueueStatistics();
    }

    [RelayCommand]
    private async Task StartQueue()
    {
        Console.WriteLine(
            $"[DEBUG] StartQueue called - CanStartQueue: {CanStartQueue}, QueueState: {QueueState}, TaskCount: {RenderTasks.Count}, BlenderPath: {_blenderPath}");

        if (!CanStartQueue)
            // Console.WriteLine("[DEBUG] StartQueue aborted - CanStartQueue is false");
            return;

        if (!IsBlenderServiceReady())
        {
            // Console.WriteLine("[DEBUG] StartQueue aborted - Blender path is not ready");
            QueueStatusChanged?.Invoke(this,
                new QueueStatusChangedEventArgs(Localizer.Localizer.Instance["Toast_BlenderPathRequired"]));
            return;
        }

        // 开始队列时清空所有预备删除状态
        ClearPendingDeletionStates();

        // 停止队列：重置所有启用且有效的任务状态，从头开始
        foreach (var task in RenderTasks.Where(t => t.Enable && t.IsValid))
        {
            if (task.Status == RenderTaskStatus.Running) task.StopRender();

            // 重置启用的任务状态为等待中，从头开始
            task.Status = RenderTaskStatus.Pending;
            // 重置进度信息
            task.ResetProgress();
        }

        QueueState = QueueState.Running;
        QueueStatusText = "Queue_Running";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Started"));

        // 清空帧渲染时间记录，重新开始计算
        _recentFrameRenderTimes.Clear();

        // 启动剩余时间更新定时器
        _remainingTimeTimer?.Start();

        // 启动第一个任务
        await StartNextAvailableTasks();
    }

    [RelayCommand]
    private void StopQueue()
    {
        if (!CanStopQueue) return;

        Console.WriteLine("[RenderQueueViewModel] Stopping queue...");

        // 立即更新UI状态，提供即时反馈
        QueueState = QueueState.Idle;
        QueueStatusText = "Queue_Stopped";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Stopped"));

        // 清除当前渲染任务
        CurrentRenderingTask = null;

        // 停止剩余时间更新定时器
        _remainingTimeTimer?.Stop();

        // 清空帧渲染时间记录
        _recentFrameRenderTimes.Clear();

        // 清空剩余时间显示
        RemainingTimeText = string.Empty;

        // 清除暂停状态记录
        _pausedTask = null;
        _pausedFrame = 0;

        // 异步停止所有运行中的任务，不阻塞UI
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running)) 
                {
                    task.StopRender();
                }
                Console.WriteLine("[RenderQueueViewModel] Queue stopped successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderQueueViewModel] Error stopping queue: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void PauseQueue()
    {
        if (!CanPauseQueue) return;

        Console.WriteLine("[RenderQueueViewModel] Pausing queue...");

        // 记录当前渲染状态
        if (CurrentRenderingTask != null && CurrentRenderingTask.Status == RenderTaskStatus.Running)
        {
            _pausedTask = CurrentRenderingTask;
            _pausedFrame = CurrentRenderingTask.CurrentFrame;
            Console.WriteLine(
                $"[RenderQueueViewModel] Paused at task: {Path.GetFileName(_pausedTask.BlendFilePath)}, frame: {_pausedFrame}");
        }

        // 立即更新UI状态，提供即时反馈
        QueueState = QueueState.Paused;
        QueueStatusText = "Queue_Paused";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Paused"));

        // 停止剩余时间更新定时器
        _remainingTimeTimer?.Stop();

        // 异步停止所有运行中的任务，不阻塞UI
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
                {
                    await task.PauseRenderAsync();
                }
                Console.WriteLine("[RenderQueueViewModel] Queue paused successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderQueueViewModel] Error pausing queue: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private async Task ResumeQueue()
    {
        if (!CanResumeQueue) return;

        Console.WriteLine("[RenderQueueViewModel] Resuming queue...");

        QueueState = QueueState.Running;
        QueueStatusText = "Queue_Running";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("Queue_Resumed"));

        // 启动剩余时间更新定时器
        _remainingTimeTimer?.Start();

        // 从暂停的状态继续
        await StartNextAvailableTasks();

        Console.WriteLine("[RenderQueueViewModel] Queue resumed successfully");
    }

    [RelayCommand]
    private void MoveTaskUp()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index > 0) RenderTasks.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveTaskDown()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index < RenderTasks.Count - 1) RenderTasks.Move(index, index + 1);
    }

    [RelayCommand]
    private void MoveTaskToTop()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index > 0) RenderTasks.Move(index, 0);
    }

    [RelayCommand]
    private void MoveTaskToBottom()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index < RenderTasks.Count - 1) RenderTasks.Move(index, RenderTasks.Count - 1);
    }

    [RelayCommand]
    private void SetPostRenderBehavior(string behavior)
    {
        if (Enum.TryParse<PostRenderBehavior>(behavior, out var parsedBehavior))
        {
            PostRenderBehavior = parsedBehavior;
            OnPropertyChanged(nameof(PostRenderBehaviorIcon));
            Console.WriteLine($"[RenderQueueViewModel] Post-render behavior set to: {parsedBehavior}");
        }
    }

    [RelayCommand]
    private void CopyTask(RenderTaskViewModel? taskToCopy)
    {
        if (taskToCopy == null) return;

        try
        {
            // 创建新任务，复制所有属性
            var newTask = new RenderTaskViewModel(
                taskToCopy.BlendFilePath,
                taskToCopy.StartFrame,
                taskToCopy.EndFrame,
                taskToCopy.AutoStart,
                taskToCopy.OverrideFrameRange);

            // 复制所有设置
            newTask.Enable = taskToCopy.Enable;
            
            // 保存场景覆写设置，稍后在文件属性加载完成后设置
            var savedOverrideScene = taskToCopy.OverrideScene;
            var savedSelectedSceneName = taskToCopy.SelectedSceneName;
            
            // 设置全局参数
            newTask.SetGlobalRenderTimeout(_globalRenderTimeoutSeconds);
            newTask.SetGlobalMaxRetryAttempts(_globalMaxRetryAttempts);
            newTask.SetVideoCodec(_videoCodec);
            newTask.SetVideoQuality(_videoQuality);
            newTask.SetProcessService(_processService);

            // 添加到队列
            RenderTasks.Add(newTask);

            // 订阅任务事件
            SubscribeToTaskEvents(newTask);

            // 设置队列运行状态
            newTask.SetQueueRunningState(QueueState == QueueState.Running);

            // 选择新复制的任务
            SelectedTask = newTask;

            // 异步加载文件属性
            if (IsBlenderServiceReady())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await newTask.LoadFilePropertiesAsync(_blenderPath!);
                        
                        // 文件属性加载完成后，设置场景覆写属性
                        if (savedOverrideScene && !string.IsNullOrEmpty(savedSelectedSceneName))
                        {
                            // 在UI线程上设置场景覆写
                            Dispatcher.UIThread.Post(() =>
                            {
                                newTask.OverrideScene = savedOverrideScene;
                                newTask.SelectedSceneName = savedSelectedSceneName;
                                Console.WriteLine($"[RenderQueueViewModel] ✅ Scene override restored: {savedSelectedSceneName}");
                            });
                        }
                        
                        Console.WriteLine($"[RenderQueueViewModel] ✅ Copied task file properties loaded: {Path.GetFileName(newTask.BlendFilePath)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RenderQueueViewModel] ❌ Failed to load file properties for copied task {Path.GetFileName(newTask.BlendFilePath)}: {ex.Message}");
                    }
                });
            }

            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskCopied"], Path.GetFileName(taskToCopy.BlendFilePath)));

            Console.WriteLine($"[RenderQueueViewModel] ✅ Task copied successfully: {Path.GetFileName(taskToCopy.BlendFilePath)}");
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskCopyFailed"], ex.Message));
            Console.WriteLine($"[RenderQueueViewModel] ❌ Failed to copy task: {ex.Message}");
        }
    }



    public void SetBlenderService(BlenderProcessService? blenderProcessService)
    {
        // 先释放旧的Blender进程服务（如果存在）
        if (_blenderProcessService != null)
        {
            Console.WriteLine(
                $"[RenderQueueViewModel] Disposing old Blender process service");
            _blenderProcessService.Dispose();
        }

        _blenderProcessService = blenderProcessService;
        // 注意：BlenderVideoService现在需要IBlenderProcess，这里暂时设为null
        // 视频生成时会创建临时的BlenderVideoService
        _blenderVideoService = null;
        
        if (blenderProcessService != null)
        {
            Console.WriteLine($"[RenderQueueViewModel] BlenderProcessService set successfully");
            // 重新初始化文件监控，因为现在有了Blender路径
            InitializeBlenderDataWatcher();
        }
        else
        {
            Console.WriteLine("[RenderQueueViewModel] BlenderService set to null - cleaning up");
            // 清理文件监控，因为没有Blender路径
            CleanupBlenderDataWatcher();
        }
    }

    /// <summary>
    ///     设置Blender路径（不创建长期运行的服务）
    /// </summary>
    public void SetBlenderPath(string blenderPath)
    {
        // 先释放旧的服务（如果存在）
        if (_blenderProcessService != null)
        {
            Console.WriteLine(
                $"[RenderQueueViewModel] Disposing old Blender process service");
            _blenderProcessService.Dispose();
        }

        if (_processService != null)
        {
            Console.WriteLine("[RenderQueueViewModel] Disposing old process service");
            _processService.Dispose();
        }

        _blenderPath = blenderPath;
        _blenderVideoService = null; // 视频服务需要Blender进程实例，暂时设为null

        // 创建新的进程管理服务
        _processService = new BlenderProcessService(blenderPath);
        Console.WriteLine($"[RenderQueueViewModel] Blender path and process service set successfully: {blenderPath}");

        // 重新初始化文件监控，因为现在有了Blender路径
        InitializeBlenderDataWatcher();
    }

    /// <summary>
    ///     检查BlenderService是否已准备就绪
    /// </summary>
    public bool IsBlenderServiceReady()
    {
        return !string.IsNullOrEmpty(_blenderPath) && File.Exists(_blenderPath);
    }


    private async Task StartNextAvailableTasks()
    {
        if (QueueState != QueueState.Running) return;

        // 单任务模式：先停止所有正在运行的任务，然后启动下一个
        var runningTasks = RenderTasks.Where(t => t.Status == RenderTaskStatus.Running).ToList();

        foreach (var task in runningTasks) task.StopRender();

        // 等待一下确保任务停止
        await Task.Delay(100);

        RenderTaskViewModel? taskToStart = null;

        // 如果有暂停的任务，优先恢复暂停的任务
        if (_pausedTask != null && _pausedTask.Enable && _pausedTask.IsValid)
        {
            taskToStart = _pausedTask;
            Console.WriteLine(
                $"[RenderQueueViewModel] Resuming paused task: {Path.GetFileName(_pausedTask.BlendFilePath)} from frame {_pausedFrame}");
        }
        else
        {
            // 启动下一个待处理且启用且有效的任务
            taskToStart =
                RenderTasks.FirstOrDefault(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid);
        }

        if (taskToStart == null)
        {
            // 没有更多任务，清除当前渲染任务
            CurrentRenderingTask = null;
            return;
        }

        // 设置当前渲染任务
        CurrentRenderingTask = taskToStart;

        var taskCopy = taskToStart; // 避免闭包问题
        var runningTask = Task.Run(async () =>
        {
            try
            {
                // 使用新的进程管理服务创建渲染进程
                var renderProcess = await _processService!.CreateRenderProcessAsync();

                try
                {
                    // 如果是恢复暂停的任务，从指定帧开始
                    if (_pausedTask == taskCopy && _pausedFrame > 0)
                    {
                        await taskCopy.ResumeRenderAsync(renderProcess, _pausedFrame);
                        // 清除暂停状态记录
                        _pausedTask = null;
                        _pausedFrame = 0;
                    }
                    else
                    {
                        await taskCopy.StartRenderAsync(renderProcess);
                    }
                }
                finally
                {
                    // 渲染完成后停止并释放进程
                    await renderProcess.StopAsync();
                    _processService.UnregisterProcess(renderProcess.ProcessId);
                    renderProcess.Dispose();
                }
            }
            catch (Exception)
            {
                // 错误处理已在RenderTaskViewModel中完成
            }
            finally
            {
                // 任务完成后，尝试启动下一个任务
                if (AutoStartNext && QueueState == QueueState.Running) await StartNextAvailableTasks();
            }
        });

        lock (_queueLock)
        {
            _runningTasks.Add(runningTask);
        }
    }

    private void SubscribeToTaskEvents(RenderTaskViewModel task)
    {
        task.StatusChanged += OnTaskStatusChanged;
        task.ProgressChanged += OnTaskProgressChanged;
        task.RefreshRequested += OnTaskRefreshRequested;
        task.EnableChanged += OnTaskEnableChanged;
        task.OverrideFrameRangeChanged += OnTaskOverrideFrameRangeChanged;
        task.OverrideSceneChanged += OnTaskOverrideSceneChanged;
        task.SceneSelectionChanged += OnTaskSceneSelectionChanged;
        task.FrameRangeChanged += OnTaskFrameRangeChanged;
        task.OpenInBlenderRequested += OnTaskOpenInBlenderRequested;
        task.OpenFileDirectoryRequested += OnTaskOpenFileDirectoryRequested;
    }

    private void UnsubscribeFromTaskEvents(RenderTaskViewModel task)
    {
        task.StatusChanged -= OnTaskStatusChanged;
        task.ProgressChanged -= OnTaskProgressChanged;
        task.RefreshRequested -= OnTaskRefreshRequested;
        task.EnableChanged -= OnTaskEnableChanged;
        task.OverrideFrameRangeChanged -= OnTaskOverrideFrameRangeChanged;
        task.OverrideSceneChanged -= OnTaskOverrideSceneChanged;
        task.SceneSelectionChanged -= OnTaskSceneSelectionChanged;
        task.FrameRangeChanged -= OnTaskFrameRangeChanged;
        task.OpenInBlenderRequested -= OnTaskOpenInBlenderRequested;
        task.OpenFileDirectoryRequested -= OnTaskOpenFileDirectoryRequested;
    }

    private void OnTaskStatusChanged(object? sender, RenderTaskStatusChangedEventArgs e)
    {
        UpdateQueueStatistics();

        var task = sender as RenderTaskViewModel;
        if (task != null) TaskCompleted?.Invoke(this, new TaskCompletedEventArgs(task, e.Status));
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        // 记录帧完成时间用于剩余时间计算
        if (e.CurrentFrame > 0) RecordFrameCompletion(e.CurrentFrame, e.FrameRenderTime);

        // 进度变化时只需要通知UI更新计算属性
        OnPropertyChanged(nameof(OverallQueueProgress));
        OnPropertyChanged(nameof(OverallQueueProgressInt));
        OnPropertyChanged(nameof(CompletedFrames));
    }

    private async void OnTaskRefreshRequested(object? sender, EventArgs e)
    {
        var task = sender as RenderTaskViewModel;
        if (task == null || !IsBlenderServiceReady()) return;

        Console.WriteLine($"[RenderQueueViewModel] Task refresh requested for: {Path.GetFileName(task.BlendFilePath)}");

        try
        {
            // 停止当前任务（如果正在运行）
            if (task.Status == RenderTaskStatus.Running) 
            {
                task.StopRender();
                Console.WriteLine($"[RenderQueueViewModel] Stopped running task before refresh");
            }

            // 使用新的刷新方法，不销毁任务实例
            await task.RefreshFilePropertiesAsync(_blenderPath!);

            // 更新队列统计信息
            UpdateQueueStatistics();

            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskReloaded"], Path.GetFileName(task.BlendFilePath)));
            
            Console.WriteLine($"[RenderQueueViewModel] ✅ Task refreshed successfully without recreation");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Task refresh failed: {ex.Message}");
            StatusMessageChanged?.Invoke(this,
                string.Format(Localizer.Localizer.Instance["Toast_TaskReloadFailed"], ex.Message));
        }
    }

    private void OnTaskEnableChanged(object? sender, EventArgs e)
    {
        // 当任务的 Enable 状态变化时，自动保存数据
        AutoSaveQueueData();

        // 更新队列统计信息
        UpdateQueueStatistics();

        // 通知按钮状态属性变更
        OnPropertyChanged(nameof(CanStartQueue));
        OnPropertyChanged(nameof(CanShowStartQueue));

        Console.WriteLine("[RenderQueueViewModel] Task enable state changed, auto-saving data");
    }

    private void OnTaskOverrideFrameRangeChanged(object? sender, EventArgs e)
    {
        // 当任务的覆写帧范围状态变化时，自动保存数据
        AutoSaveQueueData();

        // 更新队列统计信息
        UpdateQueueStatistics();

        Console.WriteLine("[RenderQueueViewModel] Task override frame range state changed, auto-saving data");
    }

    private void OnTaskOverrideSceneChanged(object? sender, EventArgs e)
    {
        // 当任务的覆写场景状态变化时，自动保存数据
        AutoSaveQueueData();
        Console.WriteLine("[RenderQueueViewModel] Task override scene state changed, auto-saving data");
    }

    private void OnTaskSceneSelectionChanged(object? sender, EventArgs e)
    {
        // 当任务的场景选择变化时，自动保存数据
        AutoSaveQueueData();
        Console.WriteLine("[RenderQueueViewModel] Task scene selection changed, auto-saving data");
    }

    private void OnTaskFrameRangeChanged(object? sender, EventArgs e)
    {
        // 当任务的帧范围变化时，自动保存数据
        AutoSaveQueueData();

        // 更新队列统计信息
        UpdateQueueStatistics();

        Console.WriteLine("[RenderQueueViewModel] Task frame range changed, auto-saving data");
    }

    private void OnTaskOpenInBlenderRequested(object? sender, OpenInBlenderRequestedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_blenderPath))
            {
                Console.WriteLine("[RenderQueueViewModel] ❌ Blender path is empty");
                return;
            }

            if (!File.Exists(e.FilePath))
            {
                Console.WriteLine($"[RenderQueueViewModel] ❌ File does not exist: {e.FilePath}");
                return;
            }

            // 检测并选择最佳的Blender可执行文件
            var blenderExecutable = GetBestBlenderExecutable(_blenderPath);

            // 启动Blender进程打开文件（独立进程，不关联到程序本体）
            var startInfo = new ProcessStartInfo
            {
                FileName = blenderExecutable,
                Arguments = $"\"{e.FilePath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = false
            };

            // 启动独立进程，不等待其结束
            var process = Process.Start(startInfo);
            if (process != null)
                // 立即释放进程句柄，让进程完全独立运行
                process.Dispose();

            Console.WriteLine(
                $"[RenderQueueViewModel] ✅ Opened file in Blender: {e.FilePath} (using {Path.GetFileName(blenderExecutable)})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error opening file in Blender: {ex.Message}");
        }
    }

    private void OnTaskOpenFileDirectoryRequested(object? sender, OpenSysDirectoryRequestedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(e.FilePath))
            {
                Console.WriteLine("[RenderQueueViewModel] ❌ File path is null or empty");
                return;
            }

            
            var success = FileSystemHelper.OpenFileDirectory(e.FilePath);
            Console.WriteLine(success
                ? $"[RenderQueueViewModel] ✅ Opened file directory: {e.FilePath}"
                : $"[RenderQueueViewModel] ❌ Failed to open file directory: {e.FilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error opening file directory: {ex.Message}");
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
            // 状态消息格式通常是 "视频生成成功: C:\path\to\video.mp4"
            if (statusMessage.Contains("视频生成成功: "))
            {
                return statusMessage.Substring(statusMessage.IndexOf("视频生成成功: ") + "视频生成成功: ".Length);
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    ///     获取最佳的Blender可执行文件，优先选择blender-launcher.exe
    /// </summary>
    /// <param name="blenderPath">当前配置的Blender路径</param>
    /// <returns>最佳的Blender可执行文件路径</returns>
    private string GetBestBlenderExecutable(string blenderPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(blenderPath);
            var fileName = Path.GetFileName(blenderPath);

            if (string.IsNullOrEmpty(directory)) return blenderPath;

            // 优先检测 blender-launcher.exe
            var launcherPath = Path.Combine(directory, "blender-launcher.exe");
            if (File.Exists(launcherPath))
            {
                Console.WriteLine($"[RenderQueueViewModel] ✅ Found blender-launcher.exe, using: {launcherPath}");
                return launcherPath;
            }

            // 如果当前就是 blender.exe，尝试查找同目录下的 blender-launcher.exe
            if (fileName.Equals("blender.exe", StringComparison.OrdinalIgnoreCase))
            {
                // 检查父目录（Steam版本通常在子目录中）
                var parentDirectory = Directory.GetParent(directory)?.FullName;
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    var parentLauncherPath = Path.Combine(parentDirectory, "blender-launcher.exe");
                    if (File.Exists(parentLauncherPath))
                    {
                        Console.WriteLine(
                            $"[RenderQueueViewModel] ✅ Found blender-launcher.exe in parent directory, using: {parentLauncherPath}");
                        return parentLauncherPath;
                    }
                }
            }

            // 如果找不到 launcher，使用原始路径
            Console.WriteLine(
                $"[RenderQueueViewModel] ⚠️ blender-launcher.exe not found, using original: {blenderPath}");
            return blenderPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[RenderQueueViewModel] ⚠️ Error detecting best Blender executable: {ex.Message}, using original: {blenderPath}");
            return blenderPath;
        }
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 当任务的 CompletedFrames 或 OverallProgress01 变化时，更新队列进度
        if (e.PropertyName == nameof(RenderTaskViewModel.CompletedFrames) ||
            e.PropertyName == nameof(RenderTaskViewModel.OverallProgress01))
        {
            OnPropertyChanged(nameof(OverallQueueProgress));
            OnPropertyChanged(nameof(CompletedFrames));
        }
    }

    private async void UpdateQueueStatistics()
    {
        ActiveTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Running);
        CompletedTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Completed);
        FailedTaskCount =
            RenderTasks.Count(t => t.Status == RenderTaskStatus.Failed || t.Status == RenderTaskStatus.Cancelled);

        // 更新队列状态文本和状态
        switch (QueueState)
        {
            case QueueState.Running:
                if (ActiveTaskCount > 0)
                {
                    QueueStatusText = $"Queue_RunningWithTasks:{ActiveTaskCount}";
                }
                else if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid))
                {
                    QueueStatusText = "Queue_Waiting";
                }
                else if (RenderTasks.Where(t => t.Enable && t.IsValid).All(t =>
                             t.Status == RenderTaskStatus.Completed ||
                             t.Status == RenderTaskStatus.Failed ||
                             t.Status == RenderTaskStatus.Cancelled))
                {
                    // 只有当所有启用的任务都完成/失败/取消时，才设置为完成状态
                    QueueStatusText = "Queue_Completed";
                    QueueState = QueueState.Completed;

                    // 触发队列完成Toast
                    var completedTasks = RenderTasks
                        .Where(t => t.Enable && t.IsValid && t.Status == RenderTaskStatus.Completed).Count();
                    var totalTasks = RenderTasks.Where(t => t.Enable && t.IsValid).Count();
                    this.ShowSuccessToast(
                        Localizer.Localizer.Instance["Queue_Completed"],
                        string.Format(Localizer.Localizer.Instance["Queue_AllTasksCompleted"], completedTasks, totalTasks));

                    // 处理队列完成后的行为
                    await HandlePostRenderBehaviorAsync();
                }
                else
                {
                    QueueStatusText = "Queue_Running";
                }

                break;

            case QueueState.Idle:
                if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid))
                    QueueStatusText = "Queue_Idle";
                else if (RenderTasks.Where(t => t.Enable && t.IsValid).Any(t =>
                             t.Status == RenderTaskStatus.Completed || t.Status == RenderTaskStatus.Failed ||
                             t.Status == RenderTaskStatus.Cancelled))
                    QueueStatusText = "Queue_Completed";
                // 不自动改变状态，让用户手动决定是否重新开始
                else
                    QueueStatusText = "Queue_Empty";

                break;

            case QueueState.Completed:
                QueueStatusText = "Queue_Completed";
                break;

            case QueueState.Paused:
                QueueStatusText = "Queue_Paused";
                break;

            case QueueState.Error:
                QueueStatusText = "Queue_Error";
                break;
        }

        // 通知计算属性更新
        OnPropertyChanged(nameof(IsQueueRunning));
        OnPropertyChanged(nameof(IsQueueActive));
        OnPropertyChanged(nameof(HasNoTasks));
        OnPropertyChanged(nameof(HasRunningTasks));
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(CompletedFrames));
        OnPropertyChanged(nameof(OverallQueueProgress));
        OnPropertyChanged(nameof(RemainingTimeText));
        OnPropertyChanged(nameof(CanStartQueue));
        OnPropertyChanged(nameof(CanStopQueue));
        OnPropertyChanged(nameof(CanPauseQueue));
        OnPropertyChanged(nameof(CanResumeQueue));
        OnPropertyChanged(nameof(CanModifyTasks));

        // 通知所有任务更新队列运行状态，影响CanRefresh属性
        var isQueueRunning = QueueState == QueueState.Running;
        foreach (var task in RenderTasks)
        {
            task.SetQueueRunningState(isQueueRunning);
        }
    }

    private async Task<string> SelectBlendFile()
    {
        // 使用文件选择器选择单个Blend文件
        var fileTypes = new[]
        {
            new FilePickerFileType("Blend Files") { Patterns = new[] { "*.blend" } }
        };

        var result = await this.SelectFile("选择 Blend 文件", fileTypes);
        return result ?? string.Empty;
    }

    private async Task<IEnumerable<string>> SelectMultipleBlendFiles()
    {
        // 使用文件选择器选择多个Blend文件
        var fileTypes = new[]
        {
            new FilePickerFileType("Blend Files") { Patterns = new[] { "*.blend" } }
        };

        var result = await this.SelectFiles("选择多个 Blend 文件", fileTypes);
        return result ?? Enumerable.Empty<string>();
    }

#if DEBUG
    private async void AddTestTaskIfExists()
    {
        try
        {
            var testBlendPath = @"C:\Users\atticus\Downloads\test_file\test_file.blend";

            if (!File.Exists(testBlendPath))
            {
                Console.WriteLine($"[DEBUG] 测试文件不存在: {testBlendPath}");
                return;
            }

            Console.WriteLine("[DEBUG] 开始等待 Blender 服务准备就绪...");

            // 等待 Blender 服务准备就绪，超时时间5秒
            var timeout = TimeSpan.FromSeconds(5);
            var startTime = DateTime.Now;

            while (_blenderProcessService == null && DateTime.Now - startTime < timeout) await Task.Delay(100); // 每100ms检查一次

            if (_blenderProcessService == null)
            {
                Console.WriteLine("[DEBUG] 等待 Blender 服务超时，跳过添加测试任务");
                return;
            }

            Console.WriteLine($"[DEBUG] Blender 服务已就绪，添加测试任务: {testBlendPath}");

            var task = new RenderTaskViewModel(testBlendPath, 1, 1);
            var task2 = new RenderTaskViewModel(testBlendPath, 1, 1);

            // 自动加载文件属性
            await task.LoadFilePropertiesAsync(_blenderPath!);
            await task2.LoadFilePropertiesAsync(_blenderPath!);

            RenderTasks.Add(task);
            RenderTasks.Add(task2);

            // 订阅任务事件
            SubscribeToTaskEvents(task);
            SubscribeToTaskEvents(task2);

            Console.WriteLine($"[DEBUG] 测试任务添加完成: {testBlendPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] 添加测试任务失败: {ex.Message}");
        }
    }
#endif

    /// <summary>
    ///     保存当前队列数据
    /// </summary>
    public async Task SaveQueueDataAsync()
    {
        try
        {
            var appData = new AppData
            {
                RenderQueue = RenderTasks.Select(task => new RenderTaskData
                {
                    RenderTask = new RenderTaskInfo
                    {
                        Filename = Path.GetFileName(task.BlendFilePath),
                        Filepath = task.BlendFilePath,
                        StartFrame = 1, // 默认值，实际不使用
                        EndFrame = 1, // 默认值，实际不使用
                        LastRenderedFrame = task.CurrentFrame,
                        Enable = task.Enable,
                        Override = task.OverrideFrameRange || task.OverrideScene
                            ? new OverrideData
                            {
                                OverrideFrameRange = task.OverrideFrameRange
                                    ? new OverrideFrameRangeData
                                    {
                                        StartFrame = task.StartFrame,
                                        EndFrame = task.EndFrame
                                    }
                                    : null,
                                OverrideScene = task.OverrideScene
                                    ? new OverrideSceneData
                                    {
                                        SceneName = task.SelectedSceneName
                                    }
                                    : null
                            }
                            : null
                    }
                }).ToList()
            };

            var success = await _dataPersistenceService.SaveDataAsync(appData);
            if (success)
                Console.WriteLine(
                    $"[RenderQueueViewModel] ✅ Queue data saved successfully - {RenderTasks.Count} tasks");
            else
                Console.WriteLine("[RenderQueueViewModel] ❌ Failed to save queue data");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error saving queue data: {ex.Message}");
        }
    }

    /// <summary>
    ///     加载队列数据
    /// </summary>
    public async Task LoadQueueDataAsync()
    {
        try
        {
            var appData = await _dataPersistenceService.LoadDataAsync();

            // 注意：设置现在由SettingsViewModel独立管理，不再从AppData加载

            // 加载渲染任务
            foreach (var taskData in appData.RenderQueue)
            {
                var taskInfo = taskData.RenderTask;

                // 不再跳过文件不存在的任务，而是标记为无效

                // 确定是否使用覆写帧范围
                var overrideFrameRange = taskInfo.Override?.OverrideFrameRange != null;
                var startFrame =
                    overrideFrameRange ? taskInfo.Override!.OverrideFrameRange!.StartFrame : 1; // 默认值，将从文件读取
                var endFrame = overrideFrameRange ? taskInfo.Override!.OverrideFrameRange!.EndFrame : 1; // 默认值，将从文件读取

                var task = new RenderTaskViewModel(
                    taskInfo.Filepath,
                    startFrame,
                    endFrame,
                    true, // AutoStart 默认为 true
                    overrideFrameRange);

                // 设置 Enable 属性
                task.Enable = taskInfo.Enable;

                // 设置视频生成相关参数
                task.SetVideoCodec(_videoCodec);
                task.SetVideoQuality(_videoQuality);
                task.SetProcessService(_processService);

                // 保存场景覆写数据，稍后在文件属性加载完成后设置
                var savedOverrideScene = taskInfo.Override?.OverrideScene;

                // 先添加到队列，不阻塞加载过程
                Console.WriteLine(
                    $"[RenderQueueViewModel] Adding task to queue: {Path.GetFileName(taskInfo.Filepath)}");
                Console.WriteLine(
                    $"[RenderQueueViewModel] Task initial state - IsLoading: {task.ScenePropertiesView.IsLoading}, IsLoaded: {task.ScenePropertiesView.SceneProperties.IsLoaded}, ShowEmptyState: {task.ScenePropertiesView.ShowEmptyState}");

                RenderTasks.Add(task);
                SubscribeToTaskEvents(task);

                // 设置队列运行状态，影响CanRefresh属性
                task.SetQueueRunningState(QueueState == QueueState.Running);

                // 异步加载文件属性，不等待完成
                if (IsBlenderServiceReady())
                {
                    Console.WriteLine(
                        $"[RenderQueueViewModel] Starting async file properties loading for: {Path.GetFileName(taskInfo.Filepath)}");

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 设置加载状态
                            Dispatcher.UIThread.Post(() =>
                            {
                                Console.WriteLine(
                                    $"[RenderQueueViewModel] Setting loading state for: {Path.GetFileName(taskInfo.Filepath)}");
                                task.ScenePropertiesView.IsLoading = true;
                                task.ScenePropertiesView.LoadingMessage = "SceneProperties_LoadingFileProperties";
                                Console.WriteLine(
                                    $"[RenderQueueViewModel] After setting loading - IsLoading: {task.ScenePropertiesView.IsLoading}, ShowEmptyState: {task.ScenePropertiesView.ShowEmptyState}");
                            });

                            await task.LoadFilePropertiesAsync(_blenderPath!);

                            // 文件属性加载完成后，设置场景覆写属性
                            if (savedOverrideScene != null)
                                Dispatcher.UIThread.Post(() =>
                                {
                                    task.OverrideScene = true;
                                    task.SelectedSceneName = savedOverrideScene.SceneName;
                                    Console.WriteLine(
                                        $"[RenderQueueViewModel] ✅ Scene override restored: {savedOverrideScene.SceneName}");
                                });

                            Console.WriteLine(
                                $"[RenderQueueViewModel] ✅ File properties loaded: {Path.GetFileName(taskInfo.Filepath)}");
                            Console.WriteLine(
                                $"[RenderQueueViewModel] Final state - IsLoading: {task.ScenePropertiesView.IsLoading}, IsLoaded: {task.ScenePropertiesView.SceneProperties.IsLoaded}, ShowEmptyState: {task.ScenePropertiesView.ShowEmptyState}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"[RenderQueueViewModel] ❌ Failed to load file properties for {Path.GetFileName(taskInfo.Filepath)}: {ex.Message}");

                            // 设置错误状态
                            Dispatcher.UIThread.Post(() =>
                            {
                                Console.WriteLine(
                                    $"[RenderQueueViewModel] Setting error state for: {Path.GetFileName(taskInfo.Filepath)}");
                                task.ScenePropertiesView.IsLoading = false;
                                task.ScenePropertiesView.ErrorMessage = $"加载失败: {ex.Message}";
                                Console.WriteLine(
                                    $"[RenderQueueViewModel] After setting error - IsLoading: {task.ScenePropertiesView.IsLoading}, ShowEmptyState: {task.ScenePropertiesView.ShowEmptyState}");
                            });
                        }
                    });
                }
                else
                {
                    Console.WriteLine(
                        $"[RenderQueueViewModel] ⚠️ BlenderService is null, skipping file properties loading for: {Path.GetFileName(taskInfo.Filepath)}");
                }
            }

            Console.WriteLine($"[RenderQueueViewModel] ✅ Queue data loaded successfully - {RenderTasks.Count} tasks");

            // 数据加载完成后，通知按钮状态属性变更
            OnPropertyChanged(nameof(CanStartQueue));
            OnPropertyChanged(nameof(CanShowStartQueue));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error loading queue data: {ex.Message}");
        }
    }

    /// <summary>
    ///     自动保存队列数据（在任务变化时调用）
    /// </summary>
    private async void AutoSaveQueueData()
    {
        try
        {
            await SaveQueueDataAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error in auto-save: {ex.Message}");
        }
    }

    /// <summary>
    ///     初始化Blender数据文件监控
    /// </summary>
    private void InitializeBlenderDataWatcher()
    {
        // 清理现有的监控器
        _blenderDataWatcher?.Dispose();
        _blenderDataWatcher = null;

        if (string.IsNullOrEmpty(_blenderPath)) return;

        try
        {
            // 获取应用程序目录
            // var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var appDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BlenderRenderQueue"
            );
            var blenderDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BlenderRenderQueue",
                "data.json"
            );

            // 创建文件监控器
            _blenderDataWatcher = new FileSystemWatcher(appDataDirectory, "data_from_blender.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            // 订阅文件变化事件
            _blenderDataWatcher.Changed += OnBlenderDataFileChanged;
            _blenderDataWatcher.Created += OnBlenderDataFileChanged;

            Console.WriteLine($"[RenderQueueViewModel] ✅ File watcher initialized for: {blenderDataPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Failed to initialize file watcher: {ex.Message}");
        }
    }

    /// <summary>
    ///     清理Blender数据文件监控器
    /// </summary>
    private void CleanupBlenderDataWatcher()
    {
        try
        {
            if (_blenderDataWatcher != null)
            {
                _blenderDataWatcher.Changed -= OnBlenderDataFileChanged;
                _blenderDataWatcher.Created -= OnBlenderDataFileChanged;
                _blenderDataWatcher.Dispose();
                _blenderDataWatcher = null;
                Console.WriteLine("[RenderQueueViewModel] ✅ File watcher cleaned up");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Failed to cleanup file watcher: {ex.Message}");
        }
    }

    /// <summary>
    ///     处理Blender数据文件变化事件
    /// </summary>
    private async void OnBlenderDataFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // 延迟一下，确保文件写入完成
            await Task.Delay(500);

            Console.WriteLine($"[RenderQueueViewModel] 📁 Blender data file changed: {e.FullPath}");

            // 检查文件是否存在
            if (!File.Exists(e.FullPath))
            {
                Console.WriteLine($"[RenderQueueViewModel] ⚠️ File does not exist: {e.FullPath}");
                return;
            }

            // 读取文件内容
            var jsonContent = await File.ReadAllTextAsync(e.FullPath);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                Console.WriteLine($"[RenderQueueViewModel] ⚠️ File is empty: {e.FullPath}");
                return;
            }

            // 解析JSON - 使用AOT兼容的序列化选项
            var appData = JsonSerializer.Deserialize<AppData>(jsonContent, _jsonOptions);

            if (appData?.RenderQueue == null || !appData.RenderQueue.Any())
            {
                Console.WriteLine("[RenderQueueViewModel] ⚠️ No render tasks found in file");
                return;
            }

            // 在UI线程上处理
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // 添加新任务到队列
                    foreach (var taskData in appData.RenderQueue)
                    {
                        var taskInfo = taskData.RenderTask;

                        // 检查文件是否存在
                        if (!File.Exists(taskInfo.Filepath))
                        {
                            Console.WriteLine($"[RenderQueueViewModel] ⚠️ File does not exist: {taskInfo.Filepath}");
                            continue;
                        }

                        // 确定是否使用覆写帧范围
                        var overrideFrameRange = taskInfo.Override?.OverrideFrameRange != null;
                        var startFrame = overrideFrameRange ? taskInfo.Override!.OverrideFrameRange!.StartFrame : 1;
                        var endFrame = overrideFrameRange ? taskInfo.Override!.OverrideFrameRange!.EndFrame : 1;

                        var task = new RenderTaskViewModel(
                            taskInfo.Filepath,
                            startFrame,
                            endFrame,
                            true, // AutoStart
                            overrideFrameRange);

                        // 设置Enable属性
                        task.Enable = taskInfo.Enable;

                        // 设置视频生成相关参数
                        task.SetVideoCodec(_videoCodec);
                        task.SetVideoQuality(_videoQuality);
                        task.SetProcessService(_processService);

                        // 保存场景覆写数据
                        var savedOverrideScene = taskInfo.Override?.OverrideScene;

                        // 添加到队列
                        RenderTasks.Add(task);
                        SubscribeToTaskEvents(task);

                        // 设置队列运行状态，影响CanRefresh属性
                        task.SetQueueRunningState(QueueState == QueueState.Running);

                        // 异步加载文件属性
                        if (IsBlenderServiceReady())
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await task.LoadFilePropertiesAsync(_blenderPath!);

                                    // 设置场景覆写
                                    if (savedOverrideScene != null)
                                        Dispatcher.UIThread.Post(() =>
                                        {
                                            task.OverrideScene = true;
                                            task.SelectedSceneName = savedOverrideScene.SceneName;
                                        });
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(
                                        $"[RenderQueueViewModel] ❌ Failed to load file properties: {ex.Message}");
                                }
                            });

                        Console.WriteLine(
                            $"[RenderQueueViewModel] ✅ Added task from Blender: {Path.GetFileName(taskInfo.Filepath)}");
                    }

                    // 删除源文件，避免重复处理
                    try
                    {
                        File.Delete(e.FullPath);
                        Console.WriteLine($"[RenderQueueViewModel] 🗑️ Deleted source file: {e.FullPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RenderQueueViewModel] ⚠️ Failed to delete source file: {ex.Message}");
                    }

                    StatusMessageChanged?.Invoke(this, Localizer.Localizer.Instance["Toast_BlenderPluginDetected"]);

                    // 显示成功toast
                    this.ShowSuccessToast(
                        Localizer.Localizer.Instance["Toast_TaskAddSuccess"],
                        Localizer.Localizer.Instance["Toast_BlenderPluginDetected"]);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderQueueViewModel] ❌ Error processing Blender data file: {ex.Message}");
                    StatusMessageChanged?.Invoke(this,
                        string.Format(Localizer.Localizer.Instance["Toast_BlenderDataProcessError"], ex.Message));

                    // 显示错误toast
                    this.ShowErrorToast(
                        Localizer.Localizer.Instance["Toast_TaskAddFailedTitle"],
                        string.Format(Localizer.Localizer.Instance["Toast_BlenderDataProcessError"], ex.Message));
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error handling file change: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理队列完成后的行为
    /// </summary>
    private async Task HandlePostRenderBehaviorAsync()
    {
        if (PostRenderBehavior == PostRenderBehavior.None)
            return;

        try
        {
            string actionType;

            switch (PostRenderBehavior)
            {
                case PostRenderBehavior.Shutdown:
                    actionType = Localizer.Localizer.Instance["SystemControl_Shutdown"];
                    break;
                case PostRenderBehavior.Restart:
                    actionType = Localizer.Localizer.Instance["SystemControl_Restart"];
                    break;
                default:
                    return;
            }

            // 立即发送60秒的系统操作指令
            bool success = false;
            if (PostRenderBehavior == PostRenderBehavior.Shutdown)
            {
                success = await SystemControlHelper.ShutdownAsync(60, CancellationToken.None);
            }
            else if (PostRenderBehavior == PostRenderBehavior.Restart)
            {
                success = await SystemControlHelper.RestartAsync(60, CancellationToken.None);
            }
            
            if (success)
            {
                // 确保在UI线程中执行对话框显示
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // 显示60秒倒计时对话框，让用户有足够时间取消
                    var isCancelled = await SystemControlHelper.ShowCountdownDialogAsync(actionType, 60);
                    
                    if (isCancelled)
                    {
                        // 用户取消了操作，取消系统指令
                        await SystemControlHelper.CancelShutdownAsync();
                        Console.WriteLine($"[RenderQueueViewModel] ⚠️ {actionType} cancelled by user");
                        StatusMessageChanged?.Invoke(this, 
                            string.Format(Localizer.Localizer.Instance["SystemControl_ActionCancelled"], actionType));
                    }
                    else
                    {
                        Console.WriteLine($"[RenderQueueViewModel] ✅ {actionType} will execute in 60 seconds");
                    }
                });
            }
            else
            {
                Console.WriteLine($"[RenderQueueViewModel] ❌ Failed to schedule {actionType}");
                StatusMessageChanged?.Invoke(this, 
                    string.Format(Localizer.Localizer.Instance["SystemControl_ActionFailed"], actionType));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error handling post-render behavior: {ex.Message}");
            StatusMessageChanged?.Invoke(this, 
                string.Format(Localizer.Localizer.Instance["SystemControl_ActionError"], ex.Message));
        }
    }

    public void Dispose()
    {
        StopQueue();

        // 清理定时器
        _remainingTimeTimer?.Stop();
        _remainingTimeTimer?.Dispose();
        _remainingTimeTimer = null;

        // 清理文件监控器
        _blenderDataWatcher?.Dispose();
        _blenderDataWatcher = null;

        // 清理进程管理服务
        _processService?.Dispose();
        _processService = null;

        // 清理Blender服务
        _blenderProcessService?.Dispose();
        _blenderProcessService = null;

        foreach (var task in RenderTasks)
        {
            UnsubscribeFromTaskEvents(task);
            task.Dispose();
        }

        RenderTasks.Clear();
    }
}

// 队列状态变化事件参数
public class QueueStatusChangedEventArgs : EventArgs
{
    public string StatusMessage { get; }

    public QueueStatusChangedEventArgs(string statusMessage)
    {
        StatusMessage = statusMessage;
    }
}

// 任务完成事件参数
public class TaskCompletedEventArgs : EventArgs
{
    public RenderTaskViewModel Task { get; }
    public RenderTaskStatus Status { get; }

    public TaskCompletedEventArgs(RenderTaskViewModel task, RenderTaskStatus status)
    {
        Task = task;
        Status = status;
    }
}

// 确认对话框请求事件参数
public class ConfirmDialogRequestedEventArgs : EventArgs
{
    public string Title { get; }
    public string Content { get; }
    public string CancelButtonText { get; }
    public string ConfirmButtonText { get; }
    public Action ConfirmAction { get; }

    public ConfirmDialogRequestedEventArgs(string title, string content, string cancelButtonText,
        string confirmButtonText, Action confirmAction)
    {
        Title = title;
        Content = content;
        CancelButtonText = cancelButtonText;
        ConfirmButtonText = confirmButtonText;
        ConfirmAction = confirmAction;
    }
}
