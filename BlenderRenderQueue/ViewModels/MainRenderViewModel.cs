using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Services.BlenderService;
using Avalonia.Platform.Storage;
using System.Threading;

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
    private bool _isBlenderPathValid = false;

    [ObservableProperty]
    private bool _isFFmpegPathValid = false;

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
    private bool _isLoadingBlenderInfo = false;

    [ObservableProperty]
    private bool _isLoadingFFmpegInfo = false;

    // 内部状态
    private BlenderExeService? _blenderService;
    private CancellationTokenSource? _versionCts;

    public MainRenderViewModel()
    {
        // 订阅渲染队列事件
        RenderQueue.QueueStatusChanged += OnQueueStatusChanged;
        RenderQueue.TaskCompleted += OnTaskCompleted;

        // Windows 上尝试自动定位 Blender 和 FFmpeg
        TryAutoDetectBlender();
        TryAutoDetectFFmpeg();
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
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingBlenderInfo = false;
                    StatusMessage = $"加载Blender信息失败: {ex.Message}";
                    ClearBlenderInfo();
                });
            }
        }
    }

    private async Task LoadFFmpegInfoAsync(string ffmpegPath)
    {
        try
        {
            IsLoadingFFmpegInfo = true;
            StatusMessage = "正在加载FFmpeg信息...";

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
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
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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

    private void TryAutoDetectBlender()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (BlenderRenderQueue.Helpers.BlenderLocator.TryFindBlenderExe(out var exe))
                {
                    BlenderPath = exe;
                    StatusMessage = $"自动检测到 Blender: {exe}";
                }
                else
                {
                    // 未命中则后台异步扫描常见目录
                    _ = Task.Run(async () =>
                    {
                        var asyncExe = await BlenderRenderQueue.Helpers.BlenderLocator.FindBlenderExeAsync();
                        if (!string.IsNullOrWhiteSpace(asyncExe))
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                BlenderPath = asyncExe;
                                StatusMessage = $"异步检测到 Blender: {asyncExe}";
                            });
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"自动检测Blender失败: {ex.Message}";
        }
    }

    private void TryAutoDetectFFmpeg()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 尝试在 PATH 中查找 ffmpeg.exe
                var ffmpegExe = FindFFmpegInPath();
                if (!string.IsNullOrEmpty(ffmpegExe))
                {
                    FfmpegPath = ffmpegExe;
                    StatusMessage = $"自动检测到 FFmpeg: {ffmpegExe}";
                    return;
                }

                // 尝试在常见位置查找
                var commonPaths = new[]
                {
                    @"C:\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ffmpeg", "bin", "ffmpeg.exe")
                };

                foreach (var path in commonPaths)
                {
                    if (!File.Exists(path)) continue;
                    FfmpegPath = path;
                    StatusMessage = $"自动检测到 FFmpeg: {path}";
                    return;
                }

                StatusMessage = "未找到 FFmpeg，视频生成功能将不可用";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"自动检测FFmpeg失败: {ex.Message}";
        }
    }

    private string? FindFFmpegInPath()
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var ffmpegPath = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(ffmpegPath))
                {
                    return ffmpegPath;
                }
            }
        }
        catch
        {
            // 忽略错误
        }
        return null;
    }

    [RelayCommand]
    private async Task BrowseBlender()
    {
        var path = await this.SelectFile("选择 Blender 可执行文件", GetBlenderExecutableFileTypes());
        if (!string.IsNullOrWhiteSpace(path))
        {
            BlenderPath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseFFmpeg()
    {
        var path = await this.SelectFile("选择 FFmpeg 可执行文件", GetFFmpegExecutableFileTypes());
        if (!string.IsNullOrWhiteSpace(path))
        {
            FfmpegPath = path;
        }
    }

    [RelayCommand]
    private async Task AddSingleTask()
    {
        if (!IsBlenderPathValid)
        {
            StatusMessage = "请先设置有效的Blender路径";
            return;
        }

        var blendFile = await this.SelectFile("选择 Blend 文件", GetBlendFileTypes());
        if (!string.IsNullOrWhiteSpace(blendFile))
        {
            await AddTaskToQueue(blendFile);
        }
    }

    [RelayCommand]
    private async Task AddMultipleTasks()
    {
        if (!IsBlenderPathValid)
        {
            StatusMessage = "请先设置有效的Blender路径";
            return;
        }

        var blendFiles = await this.SelectFiles("选择多个 Blend 文件", GetBlendFileTypes());
        if (blendFiles != null && blendFiles.Any())
        {
            foreach (var blendFile in blendFiles)
            {
                await AddTaskToQueue(blendFile);
            }
        }
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

            RenderQueue.RenderTasks.Add(task);

            // 订阅任务事件
            SubscribeToTaskEvents(task);

            StatusMessage = $"已添加任务: {Path.GetFileName(blendFilePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"添加任务失败: {ex.Message}";
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

    private void OnQueueStatusChanged(object? sender, QueueStatusChangedEventArgs e)
    {
        StatusMessage = e.StatusMessage;
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

    private static IEnumerable<FilePickerFileType> GetBlendFileTypes()
    {
        return new[]
        {
            new FilePickerFileType("Blend Files") { Patterns = new[] { "*.blend" } }
        };
    }

    private static IEnumerable<FilePickerFileType> GetBlenderExecutableFileTypes()
    {
#if WINDOWS
        return new[] { new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } } };
#else
        return new[] { new FilePickerFileType("Blender") { Patterns = new[] { "blender", "*blender*" } } };
#endif
    }

    private static IEnumerable<FilePickerFileType> GetFFmpegExecutableFileTypes()
    {
#if WINDOWS
        return new[] { new FilePickerFileType("FFmpeg Executable") { Patterns = new[] { "ffmpeg.exe" } } };
#else
        return new[] { new FilePickerFileType("FFmpeg") { Patterns = new[] { "ffmpeg", "*ffmpeg*" } } };
#endif
    }

    public void Dispose()
    {
        _versionCts?.Cancel();
        _versionCts?.Dispose();

        RenderQueue.QueueStatusChanged -= OnQueueStatusChanged;
        RenderQueue.TaskCompleted -= OnTaskCompleted;

        RenderQueue.Dispose();
        _blenderService?.Dispose();
    }
}