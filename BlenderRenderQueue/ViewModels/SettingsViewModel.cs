using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Platform.Storage;
using System.Threading;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.Services.UI;
using BlenderRenderQueue.Helpers;
using SukiUI;

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

    [ObservableProperty]
    private bool _canSwitchBlender = true; // 是否可以切换Blender

    [ObservableProperty]
    private ThemeOption _baseTheme = ThemeOption.Default; // 当前主题

    [ObservableProperty]
    private bool _hasUnsavedChanges; // 是否有未保存的更改

    [ObservableProperty]
    private bool _hardwareAcceleration = true; // 硬件加速设置，默认为开启

    [ObservableProperty]
    private bool _hardwareAccelerationChanged = false; // 硬件加速是否已更改

    [ObservableProperty]
    private bool _apiEnabled = false; // API服务是否启用

    [ObservableProperty]
    private int _apiPort = 8080; // API服务端口

    [ObservableProperty]
    private bool _isApiRunning = false; // API服务是否正在运行

    [ObservableProperty]
    private string _apiUrl = string.Empty; // API服务URL

    private readonly SukiTheme _theme;

    /// <summary>
    /// 检查指定的Blender是否被选中
    /// </summary>
    public bool IsBlenderSelected(BlenderExecutable blender)
    {
        return SelectedBlenderExecutable != null &&
               SelectedBlenderExecutable.Path == blender.Path;
    }

    /// <summary>
    /// 更新运行任务状态
    /// </summary>
    public void UpdateRunningTasksStatus(bool hasRunningTasks)
    {
        // 与开始队列按钮逻辑保持一致：只有在队列空闲或完成时才允许切换Blender
        CanSwitchBlender = !hasRunningTasks;
        Console.WriteLine(
            $"[SettingsViewModel] 更新运行任务状态 - HasRunningTasks: {hasRunningTasks}, CanSwitchBlender: {CanSwitchBlender}");
    }

    /// <summary>
    /// 更新队列状态（与开始队列按钮逻辑保持一致）
    /// </summary>
    public void UpdateQueueState(QueueState queueState)
    {
        // 只有在队列空闲或完成时才允许切换Blender
        CanSwitchBlender = queueState == QueueState.Idle || queueState == QueueState.Completed;
        Console.WriteLine(
            $"[SettingsViewModel] 更新队列状态 - QueueState: {queueState}, CanSwitchBlender: {CanSwitchBlender}");
    }

    partial void OnLanguageChanged(LanguageOption value)
    {
        // 当语言设置发生变化时，立即加载新的语言
        if (value != null)
        {
            var language = value.Value;
            Localizer.Localizer.Instance.LoadLanguage(language);
        }

        // 标记有未保存的更改
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
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
            BlenderValidationMessage = "Blender_SelectExecutable";
            NotifyBlenderValidationChanged();
        }

        // 标记有未保存的更改
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
        }
    }

    // 内部状态
    private CancellationTokenSource? _versionCts;
    private readonly ISettingsPersistenceService _settingsPersistenceService = new SettingsPersistenceService();
    private bool _isLoadingSettings = false;

    // 事件：当设置发生变化时通知
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    // 事件：当初始化完成时通知
    public event EventHandler<InitializationCompletedEventArgs>? InitializationCompleted;

    // 事件：当Blender验证状态发生变化时通知
    public event EventHandler<BlenderValidationChangedEventArgs>? BlenderValidationChanged;

    // 事件：当运行任务状态发生变化时通知
    public event EventHandler<bool>? RunningTasksStatusChanged;

    // 事件：当API状态发生变化时通知
    public event EventHandler<ApiStatusChangedEventArgs>? ApiStatusChanged;

    public SettingsViewModel()
    {
        // 构造函数中不进行自动检测，等待StartInitialization调用
        _theme = new SukiTheme();

        // 订阅主题变化事件
        _theme.OnBaseThemeChanged += variant =>
        {
            var themeValue = variant.ToString();
            var themeOption = ThemeOption.FindByValue(themeValue);
            if (themeOption != null)
            {
                BaseTheme = themeOption;
            }

            // 可以在这里添加Toast通知
            Console.WriteLine($"[SettingsViewModel] Theme changed to: {variant}");
        };

        // 初始化当前主题
        var currentThemeValue = _theme.ActiveBaseTheme.ToString();
        var currentThemeOption = ThemeOption.FindByValue(currentThemeValue);
        if (currentThemeOption != null)
        {
            BaseTheme = currentThemeOption;
        }
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


    /// <summary>
    /// 验证单个Blender（用于自动检测后的验证）
    /// </summary>
    private async Task ValidateBlenderAsync(BlenderExecutable blender)
    {
        try
        {
            var svc = new BlenderCliInfoService();
            var info = await svc.GetVersionInfoAsync(blender.Path, CancellationToken.None);

            // 更新UI线程上的属性
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // 更新Blender信息
                blender.UpdateFromVersionInfo(info);
                blender.UpdateValidationStatus(true, DateTime.UtcNow);

                // 触发集合更改通知，让UI更新
                var index = BlenderExecutables.IndexOf(blender);
                if (index >= 0)
                {
                    BlenderExecutables[index] = blender;
                }

                Console.WriteLine($"[SettingsViewModel] ✅ Auto-validated Blender: {blender.Path} - {blender.Version}");
            });
        }
        catch (Exception ex)
        {
            // 验证失败，更新状态
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                blender.UpdateValidationStatus(false, DateTime.UtcNow);

                // 触发集合更改通知，让UI更新
                var index = BlenderExecutables.IndexOf(blender);
                if (index >= 0)
                {
                    BlenderExecutables[index] = blender;
                }

                Console.WriteLine(
                    $"[SettingsViewModel] ❌ Auto-validation failed for Blender: {blender.Path} - {ex.Message}");
            });
        }
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

                // 触发集合更改通知，让UI更新
                var index = BlenderExecutables.IndexOf(blender);
                if (index >= 0)
                {
                    BlenderExecutables[index] = blender;
                }

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

    private Task<bool> TryAutoDetectBlenderAsync(bool autoSelect = true)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var detectedBlenders = new List<string>();

                // 仅使用注册表扫描，不进行文件系统扫描
                if (BlenderRenderQueue.Helpers.BlenderLocator.TryFindBlenderExe(out var exe))
                {
                    detectedBlenders.Add(exe);
                }

                // 添加检测到的Blender到列表并立即验证
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

                                // 立即验证新添加的Blender
                                _ = Task.Run(async () => await ValidateBlenderAsync(blender));
                            }
                        }

                        // 只有在autoSelect为true且当前没有选中的Blender时才自动选择
                        if (autoSelect && SelectedBlenderExecutable == null && BlenderExecutables.Any())
                        {
                            SelectedBlenderExecutable = BlenderExecutables.First();
                        }
                    });
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


    [RelayCommand]
    private async Task BrowseBlender()
    {
        var path = await this.SelectFile("Blender_SelectFileDialog", GetBlenderExecutableFileTypes());
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
                // 标记有未保存的更改
                HasUnsavedChanges = true;
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
            // 标记有未保存的更改
            HasUnsavedChanges = true;
        }
    }

    [RelayCommand]
    public async Task SelectBlender(BlenderExecutable blenderExecutable)
    {
        if (blenderExecutable != null)
        {
            SelectedBlenderExecutable = blenderExecutable;
            await SaveSettingsToFileAsync();
            // 选择Blender后立即保存，所以清除未保存更改标记
            HasUnsavedChanges = false;
        }
    }


    [RelayCommand]
    private void ToggleBaseTheme()
    {
        _theme.SwitchBaseTheme();
    }

    [RelayCommand]
    private void SaveAndRestartApp()
    {
        try
        {
            // 先保存设置
            _ = Task.Run(async () => await SaveSettingsToFileAsync());
            // 使用FileSystemHelper重启应用程序
            var success = BlenderRenderQueue.Helpers.FileSystemHelper.RestartApplication();
            Console.WriteLine(success
                ? "[SettingsViewModel] ✅ Application restart initiated"
                : "[SettingsViewModel] ❌ Failed to restart application");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Error restarting application: {ex.Message}");
        }
    }

    partial void OnBaseThemeChanged(ThemeOption value)
    {
        if (value != null)
        {
            // 只有在用户手动更改时才应用主题，避免在加载设置时触发
            if (!_isLoadingSettings)
            {
                ApplyTheme(value.Value);
                // 标记有未保存的更改
                HasUnsavedChanges = true;
            }
        }
    }

    partial void OnDefaultRenderTimeoutSecondsChanged(int value)
    {
        // 标记有未保存的更改
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
        }
    }

    partial void OnMaxRetryAttemptsChanged(int value)
    {
        // 标记有未保存的更改
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
        }
    }

    partial void OnVideoCodecChanged(VideoCodecOption value)
    {
        // 标记有未保存的更改
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
        }
    }

    partial void OnVideoQualityChanged(VideoQualityOption value)
    {
        // 标记有未保存的更改
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
        }
    }

    partial void OnHardwareAccelerationChanged(bool value)
    {
        // 标记有未保存的更改
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
            // 标记硬件加速已更改，需要重启
            HardwareAccelerationChanged = true;
        }
    }

    partial void OnApiEnabledChanged(bool value)
    {
        // 更新API URL显示
        UpdateApiUrl();
        
        // 标记有未保存的更改（仅在非加载设置时）
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
        }

        // 触发API状态变化事件（仅在非加载设置时）
        if (!_isLoadingSettings)
        {
            ApiStatusChanged?.Invoke(this, new ApiStatusChangedEventArgs(ApiEnabled, ApiPort, IsApiRunning));
        }
    }

    partial void OnApiPortChanged(int value)
    {
        // 更新API URL显示
        UpdateApiUrl();
        
        // 标记有未保存的更改（仅在非加载设置时）
        if (!_isLoadingSettings)
        {
            HasUnsavedChanges = true;
        }

        // 触发API状态变化事件（仅在非加载设置时）
        if (!_isLoadingSettings)
        {
            ApiStatusChanged?.Invoke(this, new ApiStatusChangedEventArgs(ApiEnabled, ApiPort, IsApiRunning));
        }
    }

    partial void OnIsApiRunningChanged(bool value)
    {
        // 更新API URL显示
        UpdateApiUrl();

        // 触发API状态变化事件
        ApiStatusChanged?.Invoke(this, new ApiStatusChangedEventArgs(ApiEnabled, ApiPort, IsApiRunning));
    }

    private void ApplyTheme(string themeValue)
    {
        try
        {
            // 根据主题值应用相应的主题
            switch (themeValue)
            {
                case "Light":
                    // 切换到浅色主题
                    while (_theme.ActiveBaseTheme.ToString() != "Light")
                    {
                        _theme.SwitchBaseTheme();
                    }

                    break;
                case "Dark":
                    // 切换到深色主题
                    while (_theme.ActiveBaseTheme.ToString() != "Dark")
                    {
                        _theme.SwitchBaseTheme();
                    }

                    break;
                case "Auto":
                    // Auto主题切换到系统主题（Default）
                    while (_theme.ActiveBaseTheme.ToString() != "Default")
                    {
                        _theme.SwitchBaseTheme();
                    }

                    break;
            }

            Console.WriteLine($"[SettingsViewModel] Applied theme: {themeValue}, Current: {_theme.ActiveBaseTheme}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] Error applying theme: {ex.Message}");
        }
    }

    public void UpdateApiUrl()
    {
        if (ApiEnabled)
        {
            // 如果API启用，显示局域网IP地址的URL（不管是否正在运行）
            var localNetworkIp = NetworkHelper.GetLocalNetworkIpAddress();
            ApiUrl = $"http://{localNetworkIp}:{ApiPort}";
        }
        else
        {
            ApiUrl = string.Empty;
        }
    }

    [RelayCommand]
    public async Task SaveSettingsCommand()
    {
        var selectedPath = SelectedBlenderExecutable?.Path ?? string.Empty;

        // 触发设置变化事件
        SettingsChanged?.Invoke(this,
            new SettingsChangedEventArgs(DefaultRenderTimeoutSeconds, MaxRetryAttempts, VideoCodec.Value,
                VideoQuality.Value, Language.Value));

        // 保存设置到文件
        await SaveSettingsToFileAsync();

        // 标记已保存，清除未保存更改标记
        HasUnsavedChanges = false;
        // 清除硬件加速变更标记
        HardwareAccelerationChanged = false;
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
                Language = Language.Value,
                BaseTheme = BaseTheme.Value,
                UseGpu = HardwareAcceleration,
                ApiEnabled = ApiEnabled,
                ApiPort = ApiPort
            };

            var success = await _settingsPersistenceService.SaveSettingsAsync(settings);
            if (success)
            {
                Console.WriteLine(
                    $"[SettingsViewModel] ✅ Settings saved successfully - Selected Blender: {SelectedBlenderExecutable?.Path}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}, API: {ApiEnabled}@{ApiPort}");
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
            _isLoadingSettings = true;
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

                    Console.WriteLine(
                        $"[SettingsViewModel] Selected Blender: {SelectedBlenderExecutable?.Path ?? "NOT FOUND"}");
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

            // 加载主题设置
            if (!string.IsNullOrEmpty(settings.BaseTheme))
            {
                var themeOption = ThemeOption.FindByValue(settings.BaseTheme);
                if (themeOption != null)
                {
                    // 先设置属性，再应用主题
                    BaseTheme = themeOption;
                    // 延迟应用主题，确保UI已更新
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { ApplyTheme(settings.BaseTheme); });
                }
            }

            // 加载硬件加速设置
            HardwareAcceleration = settings.UseGpu;

            // 加载API设置
            ApiEnabled = settings.ApiEnabled;
            ApiPort = settings.ApiPort;
            
            // 手动触发API URL更新
            UpdateApiUrl();

            Console.WriteLine(
                $"[SettingsViewModel] ✅ Settings loaded successfully - Selected Blender: {SelectedBlenderExecutable?.Path}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}, API: {ApiEnabled}@{ApiPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Error loading settings: {ex.Message}");
        }
        finally
        {
            _isLoadingSettings = false;
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

// API状态变化事件参数
public class ApiStatusChangedEventArgs : EventArgs
{
    public bool IsEnabled { get; }
    public int Port { get; }
    public bool IsRunning { get; }

    public ApiStatusChangedEventArgs(bool isEnabled, int port, bool isRunning)
    {
        IsEnabled = isEnabled;
        Port = port;
        IsRunning = isRunning;
    }
}