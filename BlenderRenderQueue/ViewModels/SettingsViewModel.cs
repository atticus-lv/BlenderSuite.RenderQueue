using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using BlenderRenderQueue.Localizer;

namespace BlenderRenderQueue.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<BlenderExecutable> _blenderExecutables = new();

    [ObservableProperty]
    private BlenderExecutable? _selectedBlenderExecutable;

    [ObservableProperty]
    private bool _isLoadingBlenderInfo = false;

    [ObservableProperty]
    private string _blenderValidationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasBlenderValidationError = false;

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

    /// <summary>
    /// 检查指定的Blender是否被选中
    /// </summary>
    public bool IsBlenderSelected(BlenderExecutable blender)
    {
        return SelectedBlenderExecutable != null && 
               SelectedBlenderExecutable.Path == blender.Path;
    }

    partial void OnLanguageChanged(LanguageOption value)
    {
        // 当语言设置发生变化时，立即加载新的语言
        if (value != null)
        {
            var language = value.Value;
            Localizer.Localizer.Instance.LoadLanguage(language);
        }
    }

    partial void OnSelectedBlenderExecutableChanged(BlenderExecutable? value)
    {
        Console.WriteLine($"[SettingsViewModel] SelectedBlenderExecutable changed: {value?.Path ?? "NULL"}");
        
        if (value != null)
        {
            // 验证选中的Blender
            ValidateSelectedBlender(value);
        }
        else
        {
            // 清空验证状态
            HasBlenderValidationError = true;
            BlenderValidationMessage = "请选择一个Blender可执行文件";
            NotifyBlenderValidationChanged();
        }
    }

    // 内部状态
    private CancellationTokenSource? _versionCts;
    private readonly ISettingsPersistenceService _settingsPersistenceService = new SettingsPersistenceService();

    // 事件：当设置发生变化时通知
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    // 事件：当初始化完成时通知
    public event EventHandler<InitializationCompletedEventArgs>? InitializationCompleted;

    // 事件：当Blender验证状态发生变化时通知
    public event EventHandler<BlenderValidationChangedEventArgs>? BlenderValidationChanged;

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
            // 首先加载保存的设置
            await LoadSettingsFromFileAsync();
            
            // 验证已保存的Blender
            if (SelectedBlenderExecutable != null)
            {
                // 验证选中的Blender是否仍然有效
                if (SelectedBlenderExecutable.IsFileStillValid())
                {
                    ValidateSelectedBlender(SelectedBlenderExecutable);
                    blenderDetected = true;
                }
                else
                {
                    // 如果选中的Blender无效，清空选择
                    SelectedBlenderExecutable = null;
                }
            }
            
            // 如果没有有效的Blender，尝试自动检测（但不自动选中）
            if (SelectedBlenderExecutable == null)
            {
                await TryAutoDetectBlenderAsync(false); // 不自动选中
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Error during initialization: {ex.Message}");
        }

        // 通知初始化完成
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InitializationCompleted?.Invoke(this, new InitializationCompletedEventArgs(blenderDetected));
        });
    }

    private void ValidateSelectedBlender(BlenderExecutable blender)
    {
        _versionCts?.Cancel();
        _versionCts = new CancellationTokenSource();
        var ct = _versionCts.Token;

        // 重置验证状态
        HasBlenderValidationError = false;
        BlenderValidationMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(blender.Path))
        {
            HasBlenderValidationError = true;
            BlenderValidationMessage = "Blender路径为空";
            NotifyBlenderValidationChanged();
            return;
        }

        if (!File.Exists(blender.Path))
        {
            HasBlenderValidationError = true;
            BlenderValidationMessage = "指定的文件不存在";
            NotifyBlenderValidationChanged();
            return;
        }

        // 异步获取Blender版本信息
        _ = Task.Run(async () => await LoadBlenderInfoAsync(blender, ct));
    }


    private async Task LoadBlenderInfoAsync(BlenderExecutable blender, CancellationToken cancellationToken)
    {
        try
        {
            IsLoadingBlenderInfo = true;

            var svc = new BlenderCliInfoService();
            var info = await svc.GetVersionInfoAsync(blender.Path, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            // 更新UI线程上的属性
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // 更新Blender信息
                blender.UpdateFromVersionInfo(info);
                blender.UpdateValidationStatus(true, DateTime.UtcNow);

                IsLoadingBlenderInfo = false;
                HasBlenderValidationError = false;
                BlenderValidationMessage = string.Empty;
                NotifyBlenderValidationChanged();
            });
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingBlenderInfo = false;
                    blender.UpdateValidationStatus(false, DateTime.UtcNow);
                    HasBlenderValidationError = true;
                    BlenderValidationMessage = $"Blender验证失败: {ex.Message}";
                    NotifyBlenderValidationChanged();
                });
            }
        }
    }


    private void NotifyBlenderValidationChanged()
    {
        var isValid = SelectedBlenderExecutable?.IsValid ?? false;
        BlenderValidationChanged?.Invoke(this,
            new BlenderValidationChangedEventArgs(isValid, BlenderValidationMessage));
    }

    private async Task<bool> TryAutoDetectBlenderAsync(bool autoSelect = true)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var detectedBlenders = new List<string>();

                // 先尝试快速检测
                if (BlenderRenderQueue.Helpers.BlenderLocator.TryFindBlenderExe(out var exe))
                {
                    detectedBlenders.Add(exe);
                }

                // 如果快速检测失败，进行异步扫描
                var asyncExe = await BlenderRenderQueue.Helpers.BlenderLocator.FindBlenderExeAsync();
                if (!string.IsNullOrWhiteSpace(asyncExe) && !detectedBlenders.Contains(asyncExe))
                {
                    detectedBlenders.Add(asyncExe);
                }

                // 添加检测到的Blender到列表
                if (detectedBlenders.Any())
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var blenderPath in detectedBlenders)
                        {
                            // 检查是否已存在相同路径的Blender
                            var existing = BlenderExecutables.FirstOrDefault(b => b.Path == blenderPath);
                            if (existing == null)
                            {
                                var blender = BlenderExecutable.CreateDefault(blenderPath);
                                BlenderExecutables.Add(blender);
                            }
                        }
                        
                        // 只有在autoSelect为true且当前没有选中的Blender时才自动选择
                        if (autoSelect && SelectedBlenderExecutable == null && BlenderExecutables.Any())
                        {
                            SelectedBlenderExecutable = BlenderExecutables.First();
                        }
                    });
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
            // 检查是否已存在
            var existing = BlenderExecutables.FirstOrDefault(b => b.Path == path);
            if (existing != null)
            {
                // 如果已存在，选择它
                SelectedBlenderExecutable = existing;
            }
            else
            {
                // 创建新的Blender可执行文件
                var newBlender = BlenderExecutable.CreateDefault(path);
                BlenderExecutables.Add(newBlender);
                SelectedBlenderExecutable = newBlender;
            }
        }
    }

    [RelayCommand]
    private void RemoveBlender()
    {
        if (SelectedBlenderExecutable != null)
        {
            BlenderExecutables.Remove(SelectedBlenderExecutable);
            SelectedBlenderExecutable = BlenderExecutables.FirstOrDefault();
        }
    }

    [RelayCommand]
    public async Task SelectBlender(BlenderExecutable blenderExecutable)
    {
        if (blenderExecutable != null)
        {
            SelectedBlenderExecutable = blenderExecutable;
            await SaveSettings();
        }
    }


    [RelayCommand]
    private async Task SaveSettings()
    {
        var selectedPath = SelectedBlenderExecutable?.Path ?? string.Empty;

        // 触发设置变化事件
        SettingsChanged?.Invoke(this,
            new SettingsChangedEventArgs(DefaultRenderTimeoutSeconds, MaxRetryAttempts, VideoCodec.Value,
                VideoQuality.Value, Language.Value));

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
            // 去重：只保留每个路径的最新版本
            var uniqueBlenders = BlenderExecutables
                .GroupBy(b => b.Path)
                .Select(g => g.OrderByDescending(b => b.LastValidated).First())
                .ToList();

            var settings = new SettingsData
            {
                BlenderExecutables = uniqueBlenders,
                SelectedBlenderPath = SelectedBlenderExecutable?.Path ?? string.Empty,
                DefaultRenderTimeoutSeconds = DefaultRenderTimeoutSeconds,
                MaxRetryAttempts = MaxRetryAttempts,
                VideoCodec = VideoCodec.Value,
                VideoQuality = VideoQuality.Value,
                Language = Language.Value
            };

            var success = await _settingsPersistenceService.SaveSettingsAsync(settings);
            if (success)
            {
                Console.WriteLine(
                    $"[SettingsViewModel] ✅ Settings saved successfully - Selected Blender: {SelectedBlenderExecutable?.Path}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}");
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

            // 加载Blender可执行文件列表
            if (settings.BlenderExecutables != null && settings.BlenderExecutables.Any())
            {
                BlenderExecutables.Clear();
                
                // 去重：只保留每个路径的最新版本
                var uniqueBlenders = settings.BlenderExecutables
                    .GroupBy(b => b.Path)
                    .Select(g => g.OrderByDescending(b => b.LastValidated).First())
                    .ToList();
                
                foreach (var blender in uniqueBlenders)
                {
                    BlenderExecutables.Add(blender);
                }

                Console.WriteLine($"[SettingsViewModel] Loaded {BlenderExecutables.Count} unique Blender executables");

                // 设置选中的Blender
                if (!string.IsNullOrEmpty(settings.SelectedBlenderPath))
                {
                    SelectedBlenderExecutable =
                        BlenderExecutables.FirstOrDefault(b => b.Path == settings.SelectedBlenderPath);
                    
                    Console.WriteLine($"[SettingsViewModel] Selected Blender: {SelectedBlenderExecutable?.Path ?? "NOT FOUND"}");
                }
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
                    // 加载语言设置时，立即应用语言切换
                    Localizer.Localizer.Instance.LoadLanguage(settings.Language);
                }
                else
                {
                    // 如果语言选项无效，设置为默认英文
                    Language = LanguageOption.Default;
                    Localizer.Localizer.Instance.LoadLanguage(LanguageOption.Default.Value);
                }
            }
            else
            {
                // 如果没有语言设置，设置为默认英文
                Language = LanguageOption.Default;
                Localizer.Localizer.Instance.LoadLanguage(LanguageOption.Default.Value);
            }

            Console.WriteLine(
                $"[SettingsViewModel] ✅ Settings loaded successfully - Selected Blender: {SelectedBlenderExecutable?.Path}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}");
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
    public int DefaultRenderTimeoutSeconds { get; }
    public int MaxRetryAttempts { get; }
    public string VideoCodec { get; }
    public string VideoQuality { get; }
    public string Language { get; }

    public SettingsChangedEventArgs(int defaultRenderTimeoutSeconds, int maxRetryAttempts, string videoCodec,
        string videoQuality, string language)
    {
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

// Blender验证状态变化事件参数
public class BlenderValidationChangedEventArgs : EventArgs
{
    public bool IsValid { get; }
    public string Message { get; }

    public BlenderValidationChangedEventArgs(bool isValid, string message)
    {
        IsValid = isValid;
        Message = message;
    }
}