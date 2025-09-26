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
    private bool _isBlenderPathValid = false;

    [ObservableProperty]
    private string _blenderVersion = string.Empty;

    [ObservableProperty]
    private string _blenderPlatform = string.Empty;

    [ObservableProperty]
    private string _blenderBranch = string.Empty;

    [ObservableProperty]
    private string _blenderHash = string.Empty;

    [ObservableProperty]
    private bool _isLoadingBlenderInfo = false;

    [ObservableProperty]
    private int _defaultRenderTimeoutSeconds = 300; // 默认5分钟

    [ObservableProperty]
    private int _maxRetryAttempts = 3; // 默认最大重试3次

    [ObservableProperty]
    private VideoCodecOption _videoCodec = VideoCodecOption.H264; // 默认使用H264编码

    [ObservableProperty]
    private VideoQualityOption _videoQuality = VideoQualityOption.PerceptualLossless; // 默认感知无损质量

    [ObservableProperty]
    private LanguageOption _language = LanguageOption.Default; // 默认英语

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

        try
        {
            // 尝试自动检测 Blender
            blenderDetected = await TryAutoDetectBlenderAsync();
        }
        catch (Exception)
        {
            // 忽略检测过程中的错误
        }

        // 通知初始化完成
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InitializationCompleted?.Invoke(this, new InitializationCompletedEventArgs(blenderDetected));
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
    private async Task SaveSettings()
    {
        // 触发设置变化事件
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(BlenderPath, DefaultRenderTimeoutSeconds, MaxRetryAttempts, VideoCodec.Value, VideoQuality.Value, Language.Value));
        
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
                DefaultRenderTimeoutSeconds = DefaultRenderTimeoutSeconds,
                MaxRetryAttempts = MaxRetryAttempts,
                VideoCodec = VideoCodec.Value,
                VideoQuality = VideoQuality.Value,
                Language = Language.Value
            };

            var success = await _settingsPersistenceService.SaveSettingsAsync(settings);
            if (success)
            {
                Console.WriteLine($"[SettingsViewModel] ✅ Settings saved successfully - Blender: {BlenderPath}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}");
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

            if (settings.DefaultRenderTimeoutSeconds > 0)
            {
                DefaultRenderTimeoutSeconds = settings.DefaultRenderTimeoutSeconds;
            }

            if (settings.MaxRetryAttempts > 0)
            {
                MaxRetryAttempts = settings.MaxRetryAttempts;
            }

            if (!string.IsNullOrEmpty(settings.VideoCodec))
            {
                VideoCodec = settings.VideoCodec switch
                {
                    "H264" => VideoCodecOption.H264,
                    "H265" => VideoCodecOption.H265,
                    "AV1" => VideoCodecOption.AV1,
                    _ => VideoCodecOption.H264
                };
            }

            if (!string.IsNullOrEmpty(settings.VideoQuality))
            {
                VideoQuality = settings.VideoQuality switch
                {
                    "LOSSLESS" => VideoQualityOption.Lossless,
                    "PERC_LOSSLESS" => VideoQualityOption.PerceptualLossless,
                    "HIGH" => VideoQualityOption.High,
                    "MEDIUM" => VideoQualityOption.Medium,
                    "LOW" => VideoQualityOption.Low,
                    _ => VideoQualityOption.PerceptualLossless
                };
            }

            if (!string.IsNullOrEmpty(settings.Language))
            {
                var languageOption = LanguageOption.FindByValue(settings.Language);
                if (languageOption != null)
                {
                    Language = languageOption;
                }
            }

            Console.WriteLine($"[SettingsViewModel] ✅ Settings loaded successfully - Blender: {BlenderPath}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}");
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
    public int DefaultRenderTimeoutSeconds { get; }
    public int MaxRetryAttempts { get; }
    public string VideoCodec { get; }
    public string VideoQuality { get; }
    public string Language { get; }

    public SettingsChangedEventArgs(string blenderPath, int defaultRenderTimeoutSeconds, int maxRetryAttempts, string videoCodec, string videoQuality, string language)
    {
        BlenderPath = blenderPath;
        DefaultRenderTimeoutSeconds = defaultRenderTimeoutSeconds;
        MaxRetryAttempts = maxRetryAttempts;
        VideoCodec = videoCodec;
        VideoQuality = videoQuality;
        Language = language;
    }
}

// 初始化完成事件参数
public class InitializationCompletedEventArgs : EventArgs
{
    public bool IsBlenderDetected { get; }

    public InitializationCompletedEventArgs(bool isBlenderDetected)
    {
        IsBlenderDetected = isBlenderDetected;
    }
}