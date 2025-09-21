using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services.BlenderService;
using BlenderRenderQueue.Services.FFmpegService;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Models;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform.Storage;

namespace BlenderRenderQueue.ViewModels;

public partial class RenderQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<RenderTaskViewModel> _renderTasks = [];

    [ObservableProperty]
    private RenderTaskViewModel? _selectedTask;

    [ObservableProperty]
    private QueueState _queueState = QueueState.Idle;

    [ObservableProperty]
    private int _activeTaskCount = 0;

    [ObservableProperty]
    private int _completedTaskCount = 0;

    [ObservableProperty]
    private int _failedTaskCount = 0;

    [ObservableProperty]
    private string _queueStatusText = "队列空闲";

    [ObservableProperty]
    private bool _autoStartNext = true; // 自动开始下一个任务

    [ObservableProperty]
    private bool _isGeneratingVideo = false; // 是否正在生成视频

    [ObservableProperty]
    private double _videoGenerationProgress = 0.0; // 视频生成进度

    [ObservableProperty]
    private string _videoGenerationStatus = string.Empty; // 视频生成状态

    // 计算属性 - 用于UI绑定
    public bool IsQueueRunning => QueueState == QueueState.Running;
    public bool HasRunningTasks => ActiveTaskCount > 0;

    // 帧数相关的计算属性 - 只计算启用且有效的任务，使用显示用的帧范围
    public int TotalFrames => RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t => t.DisplayTotalFrames);

    public int CompletedFrames => RenderTasks.Where(t => t.Enable && t.IsValid).Sum(t =>
    {
        var totalFrames = Math.Max(0, t.DisplayTotalFrames);
        return (int)(totalFrames * t.OverallProgress01);
    });

    // 队列进度直接计算，不需要事件更新 - 只计算启用的任务
    public double OverallQueueProgress =>
        RenderTasks.Any(t => t.Enable) && TotalFrames > 0 ? (double)CompletedFrames / TotalFrames : 0.0;

    public int OverallQueueProgressInt => (int)(OverallQueueProgress * 100);

    public bool CanStartQueue
    {
        get
        {
            var canStart = (QueueState == QueueState.Idle || QueueState == QueueState.Completed) &&
                           RenderTasks.Any(t => t.Enable && t.IsValid);
            // Console.WriteLine(
            //     $"[DEBUG] CanStartQueue: {canStart} (QueueState: {QueueState}, EnabledValidTaskCount: {RenderTasks.Count(t => t.Enable && t.IsValid)})");
            return canStart;
        }
    }

    public bool CanStopQueue => QueueState == QueueState.Running;

    public bool CanModifyTasks
    {
        get
        {
            var result = QueueState == QueueState.Idle || QueueState == QueueState.Completed;
            // Console.WriteLine($"[RenderQueueViewModel] CanModifyTasks: {result} (QueueState: {QueueState})");
            return result;
        }
    }

    // 内部状态
    private readonly List<Task> _runningTasks = new();
    private BlenderExeService? _blenderService;
    private readonly IFFmpegService _ffmpegService = new FFmpegService();
    private readonly IDataPersistenceService _dataPersistenceService = new DataPersistenceService();
    private readonly object _queueLock = new object();

    // 事件
    public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<ConfirmDialogRequestedEventArgs>? ConfirmDialogRequested;

    public RenderQueueViewModel()
    {
        // 监听任务状态变化
        RenderTasks.CollectionChanged += (s, e) =>
        {
            UpdateQueueStatistics();
            // 任务集合变化时自动保存
            AutoSaveQueueData();
        };

        // 监听队列状态变化，通知计算属性更新
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(QueueState) || e.PropertyName == nameof(ActiveTaskCount) ||
                e.PropertyName == nameof(RenderTasks))
            {
                OnPropertyChanged(nameof(IsQueueRunning));
                OnPropertyChanged(nameof(HasRunningTasks));
                OnPropertyChanged(nameof(TotalFrames));
                OnPropertyChanged(nameof(CompletedFrames));
                OnPropertyChanged(nameof(CanStartQueue));
                OnPropertyChanged(nameof(CanStopQueue));
                OnPropertyChanged(nameof(CanModifyTasks));
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
        if (_blenderService == null)
        {
            StatusMessageChanged?.Invoke(this, "请先设置有效的Blender路径");
            return;
        }

        var blendFile = await SelectBlendFile();
        if (string.IsNullOrWhiteSpace(blendFile)) return;

        await AddTaskToQueue(blendFile);
    }

    [RelayCommand]
    private async Task AddMultipleTasks()
    {
        if (_blenderService == null)
        {
            StatusMessageChanged?.Invoke(this, "请先设置有效的Blender路径");
            return;
        }

        var blendFiles = await SelectMultipleBlendFiles();
        if (blendFiles == null || !blendFiles.Any()) return;

        foreach (var blendFile in blendFiles)
        {
            await AddTaskToQueue(blendFile);
        }

        Console.WriteLine($"[DEBUG] AddMultipleTasks completed - Total tasks: {RenderTasks.Count}");
    }

    private async Task AddTaskToQueue(string blendFilePath)
    {
        try
        {
            // 新任务默认不覆写帧范围，使用场景默认值
            var task = new RenderTaskViewModel(blendFilePath, 1, 1, true, false);

            // 自动加载文件属性
            if (_blenderService != null)
            {
                await task.LoadFilePropertiesAsync(_blenderService);
            }

            RenderTasks.Add(task);

            // 订阅任务事件
            SubscribeToTaskEvents(task);

            StatusMessageChanged?.Invoke(this, $"已添加任务: {Path.GetFileName(blendFilePath)}");
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this, $"添加任务失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RemoveSelectedTask()
    {
        if (SelectedTask == null) return;

        // 保存对选中任务的引用，避免在操作过程中被意外清空
        var taskToRemove = SelectedTask;

        // 如果任务正在运行，先停止
        if (taskToRemove.Status == RenderTaskStatus.Running)
        {
            taskToRemove.StopRender();
        }

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
            if (taskToRemove.Status == RenderTaskStatus.Running)
            {
                taskToRemove.StopRender();
            }

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
                    {
                        // 选择原来位置的任务（现在是下一个任务）
                        SelectedTask = RenderTasks[selectedIndex];
                    }
                    else if (selectedIndex > 0)
                    {
                        // 选择上一个任务
                        SelectedTask = RenderTasks[selectedIndex - 1];
                    }
                    else
                    {
                        // 选择第一个任务
                        SelectedTask = RenderTasks[0];
                    }
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
    /// 清除所有任务的预备删除状态
    /// </summary>
    private void ClearPendingDeletionStates()
    {
        foreach (var task in RenderTasks)
        {
            task.IsPendingDeletion = false;
        }
    }

    [RelayCommand]
    private void RemoveAllTasks()
    {
        // 请求显示确认对话框
        ConfirmDialogRequested?.Invoke(this, new ConfirmDialogRequestedEventArgs(
            "确认清空",
            $"确定要清空所有任务吗？\n\n这将删除队列中的 {RenderTasks.Count} 个任务，此操作无法撤销。",
            "取消",
            "清空",
            ExecuteRemoveAllTasks));
    }

    private void ExecuteRemoveAllTasks()
    {
        // 停止所有运行中的任务
        foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
        {
            task.StopRender();
        }

        // 取消订阅所有事件并释放资源
        foreach (var task in RenderTasks)
        {
            UnsubscribeFromTaskEvents(task);
            task.Dispose();
        }

        RenderTasks.Clear();
        SelectedTask = null;
        UpdateQueueStatistics();

        StatusMessageChanged?.Invoke(this, "已清空所有任务");
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
            $"[DEBUG] StartQueue called - CanStartQueue: {CanStartQueue}, QueueState: {QueueState}, TaskCount: {RenderTasks.Count}, BlenderService: {_blenderService != null}");

        if (!CanStartQueue)
        {
            // Console.WriteLine("[DEBUG] StartQueue aborted - CanStartQueue is false");
            return;
        }

        if (_blenderService == null)
        {
            // Console.WriteLine("[DEBUG] StartQueue aborted - BlenderService is null");
            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("需要先设置Blender路径"));
            return;
        }

        // 开始队列时清空所有预备删除状态
        ClearPendingDeletionStates();

        // 停止队列：重置所有启用且有效的任务状态，从头开始
        foreach (var task in RenderTasks.Where(t => t.Enable && t.IsValid))
        {
            if (task.Status == RenderTaskStatus.Running)
            {
                task.StopRender();
            }

            // 重置启用的任务状态为等待中，从头开始
            task.Status = RenderTaskStatus.Pending;
            task.StatusText = "等待中";
            // 重置进度信息
            task.ResetProgress();
        }

        QueueState = QueueState.Running;
        QueueStatusText = "队列运行中";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("队列已启动"));

        // 启动第一个任务
        await StartNextAvailableTasks();
    }

    [RelayCommand]
    private void StopQueue()
    {
        if (!CanStopQueue) return;

        // 停止所有运行中的任务
        foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
        {
            task.StopRender();
        }

        QueueState = QueueState.Idle;
        QueueStatusText = "队列已停止";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("队列已停止"));
    }

    [RelayCommand]
    private void MoveTaskUp()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index > 0)
        {
            RenderTasks.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveTaskDown()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index < RenderTasks.Count - 1)
        {
            RenderTasks.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void MoveTaskToTop()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index > 0)
        {
            RenderTasks.Move(index, 0);
        }
    }

    [RelayCommand]
    private void MoveTaskToBottom()
    {
        if (SelectedTask == null) return;

        var index = RenderTasks.IndexOf(SelectedTask);
        if (index < RenderTasks.Count - 1)
        {
            RenderTasks.Move(index, RenderTasks.Count - 1);
        }
    }


    [RelayCommand]
    private async Task GenerateVideoFromSelectedTask()
    {
        if (SelectedTask == null) return;

        try
        {
            // 检查 FFmpeg 是否可用
            if (!await _ffmpegService.IsFFmpegAvailableAsync())
            {
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("FFmpeg 不可用，请先设置有效的 FFmpeg 路径"));
                return;
            }

            // 获取帧路径目录
            var framePath = SelectedTask.ScenePropertiesView.SceneProperties.FramePath;
            if (string.IsNullOrEmpty(framePath))
            {
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("任务没有帧路径信息"));
                return;
            }

            var frameDirectory = Path.GetDirectoryName(framePath);
            if (string.IsNullOrEmpty(frameDirectory) || !Directory.Exists(frameDirectory))
            {
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs($"帧路径目录不存在: {frameDirectory}"));
                return;
            }

            // 检查目录中是否有图片文件
            var supportedExtensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tiff", "*.tga" };
            var hasImages = supportedExtensions.Any(ext =>
                Directory.GetFiles(frameDirectory, ext, SearchOption.TopDirectoryOnly).Length > 0);

            if (!hasImages)
            {
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs($"帧路径目录中没有找到图片文件: {frameDirectory}"));
                return;
            }

            // 获取帧率
            var fps = SelectedTask.ScenePropertiesView.SceneProperties.Fps ?? 24.0; // 默认 24fps

            // 生成输出视频路径：与输入目录同名，放在同一层级
            var inputDirectoryName = Path.GetFileName(frameDirectory);
            var parentDirectory = Path.GetDirectoryName(frameDirectory);
            var outputVideoPath = Path.Combine(parentDirectory ?? "", $"{inputDirectoryName}.mp4");

            // 开始生成视频
            IsGeneratingVideo = true;
            VideoGenerationProgress = 0.0;
            VideoGenerationStatus = "正在生成视频...";
            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs($"开始生成视频: {outputVideoPath}"));

            // 生成视频
            var success = await _ffmpegService.GenerateVideoFromImagesAsync(
                frameDirectory,
                outputVideoPath,
                fps,
                progress =>
                {
                    // 更新进度
                    VideoGenerationProgress = progress;
                    VideoGenerationStatus = $"生成中:";
                });

            if (success)
            {
                VideoGenerationStatus = "视频生成完成";
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs($"视频生成完成: {outputVideoPath}"));
            }
            else
            {
                VideoGenerationStatus = "视频生成失败";
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("视频生成失败"));
            }
        }
        catch (Exception ex)
        {
            VideoGenerationStatus = $"生成失败: {ex.Message}";
            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs($"生成视频时出错: {ex.Message}"));
        }
        finally
        {
            IsGeneratingVideo = false;
        }
    }

    public void SetBlenderService(BlenderExeService blenderService)
    {
        _blenderService = blenderService;
        // Console.WriteLine("[RenderQueueViewModel] BlenderService set successfully");
    }

    /// <summary>
    /// 检查BlenderService是否已准备就绪
    /// </summary>
    public bool IsBlenderServiceReady()
    {
        return _blenderService != null;
    }

    public void SetFFmpegPath(string? ffmpegPath)
    {
        _ffmpegService.SetFFmpegPath(ffmpegPath);
    }

    private async Task StartNextAvailableTasks()
    {
        if (QueueState != QueueState.Running)
        {
            return;
        }

        // 单任务模式：先停止所有正在运行的任务，然后启动下一个
        var runningTasks = RenderTasks.Where(t => t.Status == RenderTaskStatus.Running).ToList();

        foreach (var task in runningTasks)
        {
            task.StopRender();
        }

        // 等待一下确保任务停止
        await Task.Delay(100);

        // 启动下一个待处理且启用且有效的任务
        var pendingTask =
            RenderTasks.FirstOrDefault(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid);
        if (pendingTask == null)
        {
            return;
        }

        var taskCopy = pendingTask; // 避免闭包问题
        var runningTask = Task.Run(async () =>
        {
            try
            {
                // 为每个任务创建独立的BlenderExeService实例
                using var blenderService = new BlenderExeService(_blenderService!.BlenderPath);
                await taskCopy.StartRenderAsync(blenderService);
            }
            catch (Exception)
            {
                // 错误处理已在RenderTaskViewModel中完成
            }
            finally
            {
                // 任务完成后，尝试启动下一个任务
                if (AutoStartNext && QueueState == QueueState.Running)
                {
                    await StartNextAvailableTasks();
                }
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
    }

    private void OnTaskStatusChanged(object? sender, RenderTaskStatusChangedEventArgs e)
    {
        UpdateQueueStatistics();

        var task = sender as RenderTaskViewModel;
        if (task != null)
        {
            TaskCompleted?.Invoke(this, new TaskCompletedEventArgs(task, e.Status));
        }
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        // 进度变化时只需要通知UI更新计算属性
        OnPropertyChanged(nameof(OverallQueueProgress));
        OnPropertyChanged(nameof(OverallQueueProgressInt));
        OnPropertyChanged(nameof(CompletedFrames));
    }

    private async void OnTaskRefreshRequested(object? sender, EventArgs e)
    {
        var task = sender as RenderTaskViewModel;
        if (task == null || _blenderService == null) return;

        Console.WriteLine($"[RenderQueueViewModel] Task refresh requested for: {Path.GetFileName(task.BlendFilePath)}");

        try
        {
            // 保存当前选中任务的索引和文件路径
            var currentIndex = RenderTasks.IndexOf(task);
            var filePath = task.BlendFilePath;
            var wasSelected = SelectedTask == task; // 保存是否被选中的状态

            // 停止当前任务（如果正在运行）
            if (task.Status == RenderTaskStatus.Running)
            {
                task.StopRender();
            }

            // 取消订阅事件并释放资源
            UnsubscribeFromTaskEvents(task);
            task.Dispose();

            // 创建新的任务实例
            var newTask = new RenderTaskViewModel(filePath, 1, 1, true, false);

            // 重新加载文件属性
            await newTask.LoadFilePropertiesAsync(_blenderService);

            // 替换原任务
            RenderTasks[currentIndex] = newTask;

            // 重新订阅事件
            SubscribeToTaskEvents(newTask);

            // 如果这是之前选中的任务，重新选中新任务
            if (wasSelected)
            {
                SelectedTask = newTask;
            }

            StatusMessageChanged?.Invoke(this, $"任务已重新加载: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this, $"重新加载任务失败: {ex.Message}");
        }
    }

    private void OnTaskEnableChanged(object? sender, EventArgs e)
    {
        // 当任务的 Enable 状态变化时，自动保存数据
        AutoSaveQueueData();

        // 更新队列统计信息
        UpdateQueueStatistics();

        Console.WriteLine($"[RenderQueueViewModel] Task enable state changed, auto-saving data");
    }

    private void OnTaskOverrideFrameRangeChanged(object? sender, EventArgs e)
    {
        // 当任务的覆写帧范围状态变化时，自动保存数据
        AutoSaveQueueData();

        // 更新队列统计信息
        UpdateQueueStatistics();

        Console.WriteLine($"[RenderQueueViewModel] Task override frame range state changed, auto-saving data");
    }

    private void OnTaskOverrideSceneChanged(object? sender, EventArgs e)
    {
        // 当任务的覆写场景状态变化时，自动保存数据
        AutoSaveQueueData();
        Console.WriteLine($"[RenderQueueViewModel] Task override scene state changed, auto-saving data");
    }

    private void OnTaskSceneSelectionChanged(object? sender, EventArgs e)
    {
        // 当任务的场景选择变化时，自动保存数据
        AutoSaveQueueData();
        Console.WriteLine($"[RenderQueueViewModel] Task scene selection changed, auto-saving data");
    }

    private void OnTaskFrameRangeChanged(object? sender, EventArgs e)
    {
        // 当任务的帧范围变化时，自动保存数据
        AutoSaveQueueData();

        // 更新队列统计信息
        UpdateQueueStatistics();

        Console.WriteLine($"[RenderQueueViewModel] Task frame range changed, auto-saving data");
    }

    private void OnTaskOpenInBlenderRequested(object? sender, OpenInBlenderRequestedEventArgs e)
    {
        try
        {
            if (_blenderService == null || string.IsNullOrEmpty(_blenderService.BlenderPath))
            {
                Console.WriteLine($"[RenderQueueViewModel] ❌ BlenderService is null or BlenderPath is empty");
                return;
            }

            if (!File.Exists(e.FilePath))
            {
                Console.WriteLine($"[RenderQueueViewModel] ❌ File does not exist: {e.FilePath}");
                return;
            }

            // 检测并选择最佳的Blender可执行文件
            var blenderExecutable = GetBestBlenderExecutable(_blenderService.BlenderPath);

            // 启动Blender进程打开文件（独立进程，不关联到程序本体）
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = blenderExecutable,
                Arguments = $"\"{e.FilePath}\"",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
                CreateNoWindow = false
            };

            // 启动独立进程，不等待其结束
            var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                // 立即释放进程句柄，让进程完全独立运行
                process.Dispose();
            }

            Console.WriteLine(
                $"[RenderQueueViewModel] ✅ Opened file in Blender: {e.FilePath} (using {Path.GetFileName(blenderExecutable)})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error opening file in Blender: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取最佳的Blender可执行文件，优先选择blender-launcher.exe
    /// </summary>
    /// <param name="blenderPath">当前配置的Blender路径</param>
    /// <returns>最佳的Blender可执行文件路径</returns>
    private string GetBestBlenderExecutable(string blenderPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(blenderPath);
            var fileName = Path.GetFileName(blenderPath);

            if (string.IsNullOrEmpty(directory))
            {
                return blenderPath;
            }

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

    private void OnTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 当任务的 CompletedFrames 或 OverallProgress01 变化时，更新队列进度
        if (e.PropertyName == nameof(RenderTaskViewModel.CompletedFrames) ||
            e.PropertyName == nameof(RenderTaskViewModel.OverallProgress01))
        {
            OnPropertyChanged(nameof(OverallQueueProgress));
            OnPropertyChanged(nameof(CompletedFrames));
        }
    }

    private void UpdateQueueStatistics()
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
                    QueueStatusText = $"运行中 ({ActiveTaskCount} 个任务)";
                }
                else if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid))
                {
                    QueueStatusText = "等待中";
                }
                else if (RenderTasks.Where(t => t.Enable && t.IsValid).All(t =>
                             t.Status == RenderTaskStatus.Completed ||
                             t.Status == RenderTaskStatus.Failed ||
                             t.Status == RenderTaskStatus.Cancelled))
                {
                    // 只有当所有启用的任务都完成/失败/取消时，才设置为完成状态
                    QueueStatusText = "队列完成";
                    QueueState = QueueState.Completed;
                }
                else
                {
                    QueueStatusText = "运行中";
                }

                break;

            case QueueState.Idle:
                if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending && t.Enable && t.IsValid))
                {
                    QueueStatusText = "队列空闲";
                }
                else if (RenderTasks.Where(t => t.Enable && t.IsValid).Any(t =>
                             t.Status == RenderTaskStatus.Completed || t.Status == RenderTaskStatus.Failed ||
                             t.Status == RenderTaskStatus.Cancelled))
                {
                    QueueStatusText = "队列完成";
                    // 不自动改变状态，让用户手动决定是否重新开始
                }
                else
                {
                    QueueStatusText = "队列为空";
                }

                break;

            case QueueState.Completed:
                QueueStatusText = "队列完成";
                break;

            case QueueState.Paused:
                QueueStatusText = "队列已暂停";
                break;

            case QueueState.Error:
                QueueStatusText = "队列错误";
                break;
        }

        // 通知计算属性更新
        OnPropertyChanged(nameof(IsQueueRunning));
        OnPropertyChanged(nameof(HasRunningTasks));
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(CompletedFrames));
        OnPropertyChanged(nameof(OverallQueueProgress));
        OnPropertyChanged(nameof(CanStartQueue));
        OnPropertyChanged(nameof(CanStopQueue));
        OnPropertyChanged(nameof(CanModifyTasks));
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

            Console.WriteLine($"[DEBUG] 开始等待 Blender 服务准备就绪...");

            // 等待 Blender 服务准备就绪，超时时间5秒
            var timeout = TimeSpan.FromSeconds(5);
            var startTime = DateTime.Now;

            while (_blenderService == null && DateTime.Now - startTime < timeout)
            {
                await Task.Delay(100); // 每100ms检查一次
            }

            if (_blenderService == null)
            {
                Console.WriteLine($"[DEBUG] 等待 Blender 服务超时，跳过添加测试任务");
                return;
            }

            Console.WriteLine($"[DEBUG] Blender 服务已就绪，添加测试任务: {testBlendPath}");

            var task = new RenderTaskViewModel(testBlendPath, 1, 1, true, false);
            var task2 = new RenderTaskViewModel(testBlendPath, 1, 1, true, false);

            // 自动加载文件属性
            await task.LoadFilePropertiesAsync(_blenderService);
            await task2.LoadFilePropertiesAsync(_blenderService);

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
    /// 保存当前队列数据
    /// </summary>
    public async Task SaveQueueDataAsync()
    {
        try
        {
            var appData = new AppData
            {
                Settings = new SettingsData
                {
                    BlenderPath = _blenderService?.BlenderPath ?? string.Empty,
                    FfmpegPath = _ffmpegService.FFmpegPath ?? string.Empty
                },
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
                        Override = (task.OverrideFrameRange || task.OverrideScene)
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
            {
                Console.WriteLine(
                    $"[RenderQueueViewModel] ✅ Queue data saved successfully - {RenderTasks.Count} tasks");
            }
            else
            {
                Console.WriteLine($"[RenderQueueViewModel] ❌ Failed to save queue data");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error saving queue data: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载队列数据
    /// </summary>
    public async Task LoadQueueDataAsync()
    {
        try
        {
            var appData = await _dataPersistenceService.LoadDataAsync();

            // 加载设置
            if (!string.IsNullOrEmpty(appData.Settings.BlenderPath))
            {
                // 设置 Blender 路径（如果服务已初始化）
                if (_blenderService != null)
                {
                    _blenderService = new BlenderExeService(appData.Settings.BlenderPath);
                }
            }

            if (!string.IsNullOrEmpty(appData.Settings.FfmpegPath))
            {
                _ffmpegService.SetFFmpegPath(appData.Settings.FfmpegPath);
            }

            // 加载渲染任务
            foreach (var taskData in appData.RenderQueue)
            {
                var taskInfo = taskData.RenderTask;

                // 不再跳过文件不存在的任务，而是标记为无效

                // 确定是否使用覆写帧范围
                bool overrideFrameRange = taskInfo.Override?.OverrideFrameRange != null;
                int startFrame =
                    overrideFrameRange ? taskInfo.Override!.OverrideFrameRange!.StartFrame : 1; // 默认值，将从文件读取
                int endFrame = overrideFrameRange ? taskInfo.Override!.OverrideFrameRange!.EndFrame : 1; // 默认值，将从文件读取

                var task = new RenderTaskViewModel(
                    taskInfo.Filepath,
                    startFrame,
                    endFrame,
                    true, // AutoStart 默认为 true
                    overrideFrameRange);

                // 设置 Enable 属性
                task.Enable = taskInfo.Enable;

                // 保存场景覆写数据，稍后在文件属性加载完成后设置
                var savedOverrideScene = taskInfo.Override?.OverrideScene;

                // 先添加到队列，不阻塞加载过程
                Console.WriteLine(
                    $"[RenderQueueViewModel] Adding task to queue: {Path.GetFileName(taskInfo.Filepath)}");
                Console.WriteLine(
                    $"[RenderQueueViewModel] Task initial state - IsLoading: {task.ScenePropertiesView.IsLoading}, IsLoaded: {task.ScenePropertiesView.SceneProperties.IsLoaded}, ShowEmptyState: {task.ScenePropertiesView.ShowEmptyState}");

                RenderTasks.Add(task);
                SubscribeToTaskEvents(task);

                // 异步加载文件属性，不等待完成
                if (_blenderService != null)
                {
                    Console.WriteLine(
                        $"[RenderQueueViewModel] Starting async file properties loading for: {Path.GetFileName(taskInfo.Filepath)}");

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 设置加载状态
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                Console.WriteLine(
                                    $"[RenderQueueViewModel] Setting loading state for: {Path.GetFileName(taskInfo.Filepath)}");
                                task.ScenePropertiesView.IsLoading = true;
                                task.ScenePropertiesView.LoadingMessage = "正在加载文件属性...";
                                Console.WriteLine(
                                    $"[RenderQueueViewModel] After setting loading - IsLoading: {task.ScenePropertiesView.IsLoading}, ShowEmptyState: {task.ScenePropertiesView.ShowEmptyState}");
                            });

                            await task.LoadFilePropertiesAsync(_blenderService);

                            // 文件属性加载完成后，设置场景覆写属性
                            if (savedOverrideScene != null)
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    task.OverrideScene = true;
                                    task.SelectedSceneName = savedOverrideScene.SceneName;
                                    Console.WriteLine(
                                        $"[RenderQueueViewModel] ✅ Scene override restored: {savedOverrideScene.SceneName}");
                                });
                            }

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
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueViewModel] ❌ Error loading queue data: {ex.Message}");
        }
    }

    /// <summary>
    /// 自动保存队列数据（在任务变化时调用）
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

    public void Dispose()
    {
        StopQueue();

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