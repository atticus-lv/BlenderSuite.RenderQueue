using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services.BlenderService;
using BlenderRenderQueue.Services.FFmpegService;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform.Storage;

namespace BlenderRenderQueue.ViewModels;

public partial class RenderQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<RenderTaskViewModel> _renderTasks = new();

    [ObservableProperty]
    private RenderTaskViewModel? _selectedTask;

    [ObservableProperty]
    private bool _isQueueRunning = false;

    [ObservableProperty]
    private int _activeTaskCount = 0;

    [ObservableProperty]
    private int _completedTaskCount = 0;

    [ObservableProperty]
    private int _failedTaskCount = 0;

    [ObservableProperty]
    private double _overallQueueProgress = 0.0;

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

    // 内部状态
    private readonly List<Task> _runningTasks = new();
    private BlenderExeService? _blenderService;
    private readonly IFFmpegService _ffmpegService = new FFmpegService();
    private string? _ffmpegPath;
    private readonly object _queueLock = new object();

    // 事件
    public event EventHandler<QueueStatusChangedEventArgs>? QueueStatusChanged;
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;

    public RenderQueueViewModel()
    {
        // 监听任务状态变化
        RenderTasks.CollectionChanged += (s, e) => UpdateQueueStatistics();
        
        // Debug 模式下添加测试任务
#if DEBUG
        AddTestTaskIfExists();
#endif
    }

    [RelayCommand]
    private async Task AddTask()
    {
        var blendFile = await SelectBlendFile();
        if (string.IsNullOrWhiteSpace(blendFile)) return;

        var task = new RenderTaskViewModel(blendFile, 1, 1, true);
        
        // 如果有Blender服务，自动加载文件属性
        if (_blenderService != null)
        {
            await task.LoadFilePropertiesAsync(_blenderService);
        }

        RenderTasks.Add(task);
        
        // 订阅任务事件
        SubscribeToTaskEvents(task);
        
        UpdateQueueStatistics();
    }

    [RelayCommand]
    private async Task AddMultipleTasks()
    {
        var blendFiles = await SelectMultipleBlendFiles();
        if (blendFiles == null || !blendFiles.Any()) return;

        foreach (var blendFile in blendFiles)
        {
            var task = new RenderTaskViewModel(blendFile, 1, 1, true);
            
            // 如果有Blender服务，自动加载文件属性
            if (_blenderService != null)
            {
                await task.LoadFilePropertiesAsync(_blenderService);
            }

            RenderTasks.Add(task);
            
            // 订阅任务事件
            SubscribeToTaskEvents(task);
        }
        
        UpdateQueueStatistics();
    }

    [RelayCommand]
    private void RemoveSelectedTask()
    {
        if (SelectedTask == null) return;

        // 如果任务正在运行，先停止
        if (SelectedTask.Status == RenderTaskStatus.Running)
        {
            SelectedTask.StopRender();
        }

        // 取消订阅事件
        UnsubscribeFromTaskEvents(SelectedTask);
        
        RenderTasks.Remove(SelectedTask);
        SelectedTask.Dispose();
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
        if (IsQueueRunning) return;
        if (!RenderTasks.Any()) return;
        if (_blenderService == null)
        {
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

        IsQueueRunning = true;
        QueueStatusText = "队列运行中";
        QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs("队列已启动"));

        // 启动第一个任务
        await StartNextAvailableTasks();
    }

    [RelayCommand]
    private void StopQueue()
    {
        if (!IsQueueRunning) return;

        // 停止所有运行中的任务
        foreach (var task in RenderTasks.Where(t => t.Status == RenderTaskStatus.Running))
        {
            task.StopRender();
        }

        IsQueueRunning = false;
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
            var framePath = SelectedTask.FileProperties.Properties.FramePath;
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
            var hasImages = supportedExtensions.Any(ext => Directory.GetFiles(frameDirectory, ext, SearchOption.TopDirectoryOnly).Length > 0);
            
            if (!hasImages)
            {
                QueueStatusChanged?.Invoke(this, new QueueStatusChangedEventArgs($"帧路径目录中没有找到图片文件: {frameDirectory}"));
                return;
            }

            // 获取帧率
            var fps = SelectedTask.FileProperties.Properties.Fps ?? 24.0; // 默认 24fps

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
                    VideoGenerationStatus = $"生成进度: {progress:P1}";
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
        _ffmpegPath = ffmpegPath;
        _ffmpegService.SetFFmpegPath(ffmpegPath);
    }

    private async Task StartNextAvailableTasks()
    {
        if (!IsQueueRunning) return;

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
        if (pendingTask == null) return;

        var taskCopy = pendingTask; // 避免闭包问题
        var runningTask = Task.Run(async () =>
        {
            try
            {
                // 为每个任务创建独立的BlenderExeService实例
                using var blenderService = new BlenderExeService(_blenderService!.BlenderPath);
                await taskCopy.StartRenderAsync(blenderService);
            }
            catch (Exception ex)
            {
                // 错误处理已在RenderTaskViewModel中完成
            }
            finally
            {
                // 任务完成后，尝试启动下一个任务
                if (AutoStartNext && IsQueueRunning)
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

        // 如果启用了自动开始下一个任务，且当前任务完成，尝试启动下一个
        if (AutoStartNext && IsQueueRunning && 
            (e.Status == RenderTaskStatus.Completed || e.Status == RenderTaskStatus.Failed || e.Status == RenderTaskStatus.Cancelled))
        {
            _ = Task.Run(async () => await StartNextAvailableTasks());
        }
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        var task = sender as RenderTaskViewModel;
        if (task != null)
        {
            System.Diagnostics.Debug.WriteLine($"任务进度变化: {task.BlendFileName} - 整体进度: {e.OverallProgress:P2}, 当前帧进度: {e.CurrentFrameProgress:P2}");
        }
        UpdateQueueStatistics();
    }

    private void UpdateQueueStatistics()
    {
        ActiveTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Running);
        CompletedTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Completed);
        FailedTaskCount = RenderTasks.Count(t => t.Status == RenderTaskStatus.Failed || t.Status == RenderTaskStatus.Cancelled);

        // 计算整体进度
        if (RenderTasks.Any())
        {
            var totalProgress = RenderTasks.Sum(t => t.OverallProgress01);
            var newProgress = totalProgress / RenderTasks.Count;
            
            // 调试信息：输出进度变化
            if (Math.Abs(newProgress - OverallQueueProgress) > 0.001) // 只有显著变化时才输出
            {
                System.Diagnostics.Debug.WriteLine($"队列进度更新: {OverallQueueProgress:P2} -> {newProgress:P2} (任务数: {RenderTasks.Count})");
            }
            
            OverallQueueProgress = newProgress;
        }
        else
        {
            OverallQueueProgress = 0.0;
        }

        // 更新队列状态文本
        if (IsQueueRunning)
        {
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
                IsQueueRunning = false;
            }
        }
        else
        {
            if (RenderTasks.Any(t => t.Status == RenderTaskStatus.Pending))
            {
                QueueStatusText = "队列空闲";
            }
            else if (RenderTasks.Any())
            {
                QueueStatusText = "队列完成";
            }
            else
            {
                QueueStatusText = "队列为空";
            }
        }
    }

    private async Task<string> SelectBlendFile()
    {
        // 这里需要从主视图模型获取文件选择功能
        // 暂时返回空字符串，实际实现需要依赖注入或事件
        return string.Empty;
    }

    private async Task<IEnumerable<string>> SelectMultipleBlendFiles()
    {
        // 这里需要从主视图模型获取文件选择功能
        // 暂时返回空集合，实际实现需要依赖注入或事件
        return Enumerable.Empty<string>();
    }

#if DEBUG
    private async void AddTestTaskIfExists()
    {
        try
        {
            var testBlendPath = @"C:\Users\atticus\Downloads\test_file\test_file.blend";
            
            if (!File.Exists(testBlendPath))
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 测试文件不存在: {testBlendPath}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[DEBUG] 开始等待 Blender 服务准备就绪...");
            
            // 等待 Blender 服务准备就绪，超时时间5秒
            var timeout = TimeSpan.FromSeconds(5);
            var startTime = DateTime.Now;
            
            while (_blenderService == null && DateTime.Now - startTime < timeout)
            {
                await Task.Delay(100); // 每100ms检查一次
            }
            
            if (_blenderService == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 等待 Blender 服务超时，跳过添加测试任务");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Blender 服务已就绪，添加测试任务: {testBlendPath}");
            
            var task = new RenderTaskViewModel(testBlendPath, 1, 1, true);
            
            // 自动加载文件属性
            await task.LoadFilePropertiesAsync(_blenderService);
            
            RenderTasks.Add(task);
            
            // 订阅任务事件
            SubscribeToTaskEvents(task);
            
            System.Diagnostics.Debug.WriteLine($"[DEBUG] 测试任务添加完成: {testBlendPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] 添加测试任务失败: {ex.Message}");
        }
    }
#endif

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
