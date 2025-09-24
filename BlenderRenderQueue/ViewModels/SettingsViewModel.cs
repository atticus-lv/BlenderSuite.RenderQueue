using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services.BlenderService;
using Avalonia.Platform.Storage;
using System.Threading;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _blenderPath = string.Empty;

    [ObservableProperty]
    private string _ffmpegPath = string.Empty;

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
    private bool _isLoadingBlenderInfo = false;

    [ObservableProperty]
    private bool _isLoadingFFmpegInfo = false;

    [ObservableProperty]
    private int _defaultRenderTimeoutSeconds = 300; // 默认5分钟

    [ObservableProperty]
    private int _maxRetryAttempts = 3; // 默认最大重试3次

    [ObservableProperty]
    private string _videoGenerationMethod = "Blender"; // 默认使用Blender生成视频

    [ObservableProperty]
    private string _videoCodec = "H264"; // 默认使用H264编码

    // 内部状态
    private CancellationTokenSource? _versionCts;
    private readonly ISettingsPersistenceService _settingsPersistenceService = new SettingsPersistenceService();

    // 事件：当设置发生变化时通知
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    
    // 事件：当初始化完成时通知
    public event EventHandler<InitializationCompletedEventArgs>? InitializationCompleted;

    public SettingsViewModel()
    {
        // 构造函数中不进行自动检测，等待StartInitialization调用
    }

    public void StartInitialization()
    {
        // 开始初始化检测
        _ = Task.Run(async () => await InitializeAsync());
    }

    private async Task InitializeAsync()
    {
        var blenderDetected = false;
        var ffmpegDetected = false;

        try
        {
            // 尝试自动检测 Blender
            blenderDetected = await TryAutoDetectBlenderAsync();

            // 尝试自动检测 FFmpeg
            ffmpegDetected = await TryAutoDetectFFmpegAsync();
        }
        catch (Exception)
        {
            // 忽略检测过程中的错误
        }

        // 通知初始化完成
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InitializationCompleted?.Invoke(this, new InitializationCompletedEventArgs(blenderDetected, ffmpegDetected));
        });
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
            return;
        }

        // 异步获取FFmpeg版本信息
        _ = Task.Run(async () => await LoadFFmpegInfoAsync(value));
    }

    private async Task LoadBlenderInfoAsync(string blenderPath, CancellationToken cancellationToken)
    {
        try
        {
            IsLoadingBlenderInfo = true;

            var svc = new BlenderCliInfoService();
            var info = await svc.GetVersionInfoAsync(blenderPath, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            // 更新UI线程上的属性
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                BlenderVersion = info.Version ?? string.Empty;
                BlenderPlatform = info.Platform ?? string.Empty;
                BlenderBranch = info.Branch ?? string.Empty;
                BlenderHash = info.Hash ?? string.Empty;
                IsLoadingBlenderInfo = false;
            });
        }
        catch (Exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingBlenderInfo = false;
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
                    });
                }
            }
        }
        catch (Exception)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsLoadingFFmpegInfo = false;
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

    private async Task<bool> TryAutoDetectBlenderAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 先尝试快速检测
                if (BlenderRenderQueue.Helpers.BlenderLocator.TryFindBlenderExe(out var exe))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { BlenderPath = exe; });
                    return true;
                }

                // 如果快速检测失败，进行异步扫描
                var asyncExe = await BlenderRenderQueue.Helpers.BlenderLocator.FindBlenderExeAsync();
                if (!string.IsNullOrWhiteSpace(asyncExe))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { BlenderPath = asyncExe; });
                    return true;
                }
            }
        }
        catch
        {
            // 忽略错误
        }
        
        return false;
    }

    private Task<bool> TryAutoDetectFFmpegAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 尝试在 PATH 中查找 ffmpeg.exe
                var ffmpegExe = FindFFmpegInPath();
                if (!string.IsNullOrEmpty(ffmpegExe))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { FfmpegPath = ffmpegExe; });
                    return Task.FromResult(true);
                }

                // 尝试在常见位置查找
                var commonPaths = new[]
                {
                    @"C:\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ffmpeg", "bin",
                        "ffmpeg.exe")
                };

                foreach (var path in commonPaths)
                {
                    if (!File.Exists(path)) continue;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { FfmpegPath = path; });
                    return Task.FromResult(true);
                }
            }
        }
        catch
        {
            // 忽略错误
        }
        
        return Task.FromResult(false);
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
    private async Task SaveSettings()
    {
        // 触发设置变化事件
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(BlenderPath, FfmpegPath, DefaultRenderTimeoutSeconds, MaxRetryAttempts, VideoGenerationMethod, VideoCodec));
        
        // 保存设置到文件
        await SaveSettingsToFileAsync();
    }

    /// <summary>
    /// 保存设置到文件
    /// </summary>
    public async Task SaveSettingsToFileAsync()
    {
        try
        {
            var settings = new SettingsData
            {
                BlenderPath = BlenderPath,
                FfmpegPath = FfmpegPath,
                DefaultRenderTimeoutSeconds = DefaultRenderTimeoutSeconds,
                MaxRetryAttempts = MaxRetryAttempts,
                VideoGenerationMethod = VideoGenerationMethod,
                VideoCodec = VideoCodec
            };

            var success = await _settingsPersistenceService.SaveSettingsAsync(settings);
            if (success)
            {
                Console.WriteLine($"[SettingsViewModel] ✅ Settings saved successfully - Blender: {BlenderPath}, FFmpeg: {FfmpegPath}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}");
            }
            else
            {
                Console.WriteLine($"[SettingsViewModel] ❌ Failed to save settings");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Error saving settings: {ex.Message}");
        }
    }

    /// <summary>
    /// 从文件加载设置
    /// </summary>
    public async Task LoadSettingsFromFileAsync()
    {
        try
        {
            var settings = await _settingsPersistenceService.LoadSettingsAsync();
            
            if (!string.IsNullOrEmpty(settings.BlenderPath))
            {
                BlenderPath = settings.BlenderPath;
            }
            
            if (!string.IsNullOrEmpty(settings.FfmpegPath))
            {
                FfmpegPath = settings.FfmpegPath;
            }

            if (settings.DefaultRenderTimeoutSeconds > 0)
            {
                DefaultRenderTimeoutSeconds = settings.DefaultRenderTimeoutSeconds;
            }

            if (settings.MaxRetryAttempts > 0)
            {
                MaxRetryAttempts = settings.MaxRetryAttempts;
            }

            if (!string.IsNullOrEmpty(settings.VideoGenerationMethod))
            {
                VideoGenerationMethod = settings.VideoGenerationMethod;
            }

            if (!string.IsNullOrEmpty(settings.VideoCodec))
            {
                VideoCodec = settings.VideoCodec;
            }

            Console.WriteLine($"[SettingsViewModel] ✅ Settings loaded successfully - Blender: {BlenderPath}, FFmpeg: {FfmpegPath}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Error loading settings: {ex.Message}");
        }
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
    }
}

// 设置变化事件参数
public class SettingsChangedEventArgs : EventArgs
{
    public string BlenderPath { get; }
    public string FfmpegPath { get; }
    public int DefaultRenderTimeoutSeconds { get; }
    public int MaxRetryAttempts { get; }
    public string VideoGenerationMethod { get; }
    public string VideoCodec { get; }

    public SettingsChangedEventArgs(string blenderPath, string ffmpegPath, int defaultRenderTimeoutSeconds, int maxRetryAttempts, string videoGenerationMethod, string videoCodec)
    {
        BlenderPath = blenderPath;
        FfmpegPath = ffmpegPath;
        DefaultRenderTimeoutSeconds = defaultRenderTimeoutSeconds;
        MaxRetryAttempts = maxRetryAttempts;
        VideoGenerationMethod = videoGenerationMethod;
        VideoCodec = videoCodec;
    }
}

// 初始化完成事件参数
public class InitializationCompletedEventArgs : EventArgs
{
    public bool IsBlenderDetected { get; }
    public bool IsFFmpegDetected { get; }

    public InitializationCompletedEventArgs(bool isBlenderDetected, bool isFFmpegDetected)
    {
        IsBlenderDetected = isBlenderDetected;
        IsFFmpegDetected = isFFmpegDetected;
    }
}