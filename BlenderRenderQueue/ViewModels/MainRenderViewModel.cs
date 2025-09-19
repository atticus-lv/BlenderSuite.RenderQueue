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
        RenderQueue.StatusMessageChanged += OnRenderQueueStatusMessageChanged;
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




    private void OnQueueStatusChanged(object? sender, QueueStatusChangedEventArgs e)
    {
        StatusMessage = e.StatusMessage;
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

        RenderQueue.Dispose();
        _blenderService?.Dispose();
    }
}