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

    // 帧数相关的计算属性
    public int TotalFrames => RenderTasks.Sum(t => Math.Max(0, t.EndFrame - t.StartFrame + 1));

    public int CompletedFrames => RenderTasks.Sum(t =>
    {
        var totalFrames = Math.Max(0, t.CompletedFrames);
        return (int)(totalFrames * t.OverallProgress01);
    });

    // 队列进度直接计算，不需要事件更新
    public double OverallQueueProgress =>
        RenderTasks.Any() && TotalFrames > 0 ? (double)CompletedFrames / TotalFrames : 0.0;

    public bool CanStartQueue
    {
        get
        {
            var canStart = (QueueState == QueueState.Idle || QueueState == QueueState.Completed) && RenderTasks.Any();
            Console.WriteLine(
                $"[DEBUG] CanStartQueue: {canStart} (QueueState: {QueueState}, TaskCount: {RenderTasks.Count})");
            return canStart;
        }
    }

    public bool CanStopQueue => QueueState == QueueState.Running;
    public bool CanModifyTasks => QueueState == QueueState.Idle || QueueState == QueueState.Completed;

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
            var task = new RenderTaskViewModel(blendFilePath, 1, 1, true);

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
    private void RemoveAllTasks()
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
            Console.WriteLine("[DEBUG] StartQueue aborted - CanStartQueue is false");
            return;
        }

        if (_blenderService == null)
        {
            Console.WriteLine("[DEBUG] StartQueue aborted - BlenderService is null");
            QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("需要先设置Blender路径"));
            return;
        }

        // 停止队列：重置所有任务状态，从头开始
        foreach (var task in RenderTasks)
        {
            if (task.Status == RenderTaskStatus.Running)
            {
                task.StopRender();
            }

            // 重置所有任务状态为等待中，从头开始
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
            var framePath = SelectedTask.FileProperties.SceneProperties.FramePath;
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
            var fps = SelectedTask.FileProperties.SceneProperties.Fps ?? 24.0; // 默认 24fps

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

        // 启动下一个待处理的任务
        var pendingTask = RenderTasks.FirstOrDefault(t => t.Status == RenderTaskStatus.Pending);
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
    }

    private void UnsubscribeFromTaskEvents(RenderTaskViewModel task)
    {
        task.StatusChanged -= OnTaskStatusChanged;
        task.ProgressChanged -= OnTaskProgressChanged;
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
        OnPropertyChanged(nameof(CompletedFrames));
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
                else if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending))
                {
                    QueueStatusText = "等待中";
                }
                else
                {
                    QueueStatusText = "队列完成";
                    QueueState = QueueState.Completed;
                }

                break;

            case QueueState.Idle:
                if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending))
                {
                    QueueStatusText = "队列空闲";
                }
                else if (RenderTasks.Any(t =>
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

            var task = new RenderTaskViewModel(testBlendPath, 1, 1, true);
            var task2 = new RenderTaskViewModel(testBlendPath, 1, 1, true);

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
                        StartFrame = task.StartFrame,
                        EndFrame = task.EndFrame,
                        LastRenderedFrame = task.CurrentFrame
                    }
                }).ToList()
            };

            var success = await _dataPersistenceService.SaveDataAsync(appData);
            if (success)
            {
                Console.WriteLine($"[RenderQueueViewModel] ✅ Queue data saved successfully - {RenderTasks.Count} tasks");
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
                
                // 检查文件是否存在
                if (!File.Exists(taskInfo.Filepath))
                {
                    Console.WriteLine($"[RenderQueueViewModel] ⚠️ File not found, skipping: {taskInfo.Filepath}");
                    continue;
                }

                var task = new RenderTaskViewModel(
                    taskInfo.Filepath, 
                    taskInfo.StartFrame, 
                    taskInfo.EndFrame, 
                    true); // AutoStart 默认为 true

                // 自动加载文件属性
                if (_blenderService != null)
                {
                    await task.LoadFilePropertiesAsync(_blenderService);
                }

                RenderTasks.Add(task);
                SubscribeToTaskEvents(task);
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