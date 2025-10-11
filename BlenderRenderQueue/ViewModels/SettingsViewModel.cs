using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI;

namespace BlenderRenderQueue.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<BlenderExecutable> _blenderExecutables = new();

    [ObservableProperty]
    private BlenderExecutable? _selectedBlenderExecutable;

    [ObservableProperty]
    private bool _isLoadingBlenderInfo;

    [ObservableProperty]
    private string _blenderValidationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasBlenderValidationError;

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
    private bool _hardwareAccelerationChanged; // 硬件加速是否已更改

    [ObservableProperty]
    private bool _apiEnabled; // API服务是否启用

    [ObservableProperty]
    private int _apiPort = 8325; // API服务端口

    [ObservableProperty]
    private bool _isApiRunning; // API服务是否正在运行

    [ObservableProperty]
    private string _apiUrl = string.Empty; // API服务URL

    private readonly SukiTheme _theme;


    /// <summary>
    ///     更新队列状态（与开始队列按钮逻辑保持一致）
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
        if (!_isLoadingSettings) HasUnsavedChanges = true;
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
        if (!_isLoadingSettings) HasUnsavedChanges = true;
    }

    // 内部状态
    private CancellationTokenSource? _versionCts;
    private readonly ISettingsPersistenceService _settingsPersistenceService = new SettingsPersistenceService();
    private bool _isLoadingSettings;

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
            if (themeOption != null) BaseTheme = themeOption;

            // 可以在这里添加Toast通知
            Console.WriteLine($"[SettingsViewModel] Theme changed to: {variant}");
        };

        // 初始化当前主题
        var currentThemeValue = _theme.ActiveBaseTheme.ToString();
        var currentThemeOption = ThemeOption.FindByValue(currentThemeValue);
        if (currentThemeOption != null) BaseTheme = currentThemeOption;
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
            if (SelectedBlenderExecutable == null) await TryAutoDetectBlenderAsync(false); // 不自动选中
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Error during initialization: {ex.Message}");
        }

        // 通知初始化完成
        Dispatcher.UIThread.Post(() =>
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
    ///     验证单个Blender（用于自动检测后的验证）
    /// </summary>
    private async Task ValidateBlenderAsync(BlenderExecutable blender)
    {
        try
        {
            var svc = new BlenderCliInfoService();
            var info = await svc.GetVersionInfoAsync(blender.Path, CancellationToken.None);

            // 更新UI线程上的属性
            Dispatcher.UIThread.Post(() =>
            {
                // 更新Blender信息
                blender.UpdateFromVersionInfo(info);
                blender.UpdateValidationStatus(true, DateTime.UtcNow);

                // 触发集合更改通知，让UI更新
                var index = BlenderExecutables.IndexOf(blender);
                if (index >= 0) BlenderExecutables[index] = blender;

                Console.WriteLine($"[SettingsViewModel] ✅ Auto-validated Blender: {blender.Path} - {blender.Version}");
            });
        }
        catch (Exception ex)
        {
            // 验证失败，更新状态
            Dispatcher.UIThread.Post(() =>
            {
                blender.UpdateValidationStatus(false, DateTime.UtcNow);

                // 触发集合更改通知，让UI更新
                var index = BlenderExecutables.IndexOf(blender);
                if (index >= 0) BlenderExecutables[index] = blender;

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
            Dispatcher.UIThread.Post(() =>
            {
                // 更新Blender信息
                blender.UpdateFromVersionInfo(info);
                blender.UpdateValidationStatus(true, DateTime.UtcNow);

                // 触发集合更改通知，让UI更新
                var index = BlenderExecutables.IndexOf(blender);
                if (index >= 0) BlenderExecutables[index] = blender;

                IsLoadingBlenderInfo = false;
                HasBlenderValidationError = false;
                BlenderValidationMessage = string.Empty;
                NotifyBlenderValidationChanged();
            });
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingBlenderInfo = false;
                    blender.UpdateValidationStatus(false, DateTime.UtcNow);
                    HasBlenderValidationError = true;
                    BlenderValidationMessage = $"Blender验证失败: {ex.Message}";
                    NotifyBlenderValidationChanged();
                });
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

                // Only registry scanning is used, no file system scanning is performed
                if (BlenderLocator.TryFindBlenderExe(out var exe)) detectedBlenders.Add(exe);

                if (detectedBlenders.Count != 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var blender in from blenderPath in detectedBlenders
                                 let existing = BlenderExecutables.FirstOrDefault(b => b.Path == blenderPath)
                                 where existing == null
                                 select BlenderExecutable.CreateDefault(blenderPath))
                        {
                            BlenderExecutables.Add(blender);
                            _ = Task.Run(async () => await ValidateBlenderAsync(blender));
                        }

                        // AutoSelect is automatically selected only if autoSelect is true and there is currently no Blender selected
                        if (autoSelect && SelectedBlenderExecutable == null && BlenderExecutables.Any())
                            SelectedBlenderExecutable = BlenderExecutables.First();
                    });
                    return Task.FromResult(true);
                }
            }
        }
        catch
        {
            // ignore
        }

        return Task.FromResult(false);
    }


    [RelayCommand]
    private async Task BrowseBlender()
    {
        var path = await this.SelectFile("Blender_SelectFileDialog", GetBlenderExecutableFileTypes());
        if (!string.IsNullOrWhiteSpace(path))
        {
            var existing = BlenderExecutables.FirstOrDefault(b => b.Path == path);
            if (existing != null)
            {
                SelectedBlenderExecutable = existing;
            }
            else
            {
                var newBlender = BlenderExecutable.CreateDefault(path);
                BlenderExecutables.Add(newBlender);
                SelectedBlenderExecutable = newBlender;
                HasUnsavedChanges = true;
            }
        }
    }

    [RelayCommand]
    private void RemoveBlender()
    {
        if (SelectedBlenderExecutable == null) return;
        BlenderExecutables.Remove(SelectedBlenderExecutable);
        SelectedBlenderExecutable = BlenderExecutables.FirstOrDefault();
        HasUnsavedChanges = true;
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
            _ = Task.Run(async () => await SaveSettingsToFileAsync());
            var success = FileSystemHelper.RestartApplication();
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
        if (value == null) return;
        // Apply the theme only when the user changes it manually, avoiding triggering when the settings are loaded
        if (_isLoadingSettings) return;
        ApplyTheme(value.Value);
        HasUnsavedChanges = true;
    }

    partial void OnDefaultRenderTimeoutSecondsChanged(int value)
    {
        if (_isLoadingSettings) return;
        HasUnsavedChanges = true;
    }

    partial void OnMaxRetryAttemptsChanged(int value)
    {
        if (_isLoadingSettings) return;
        HasUnsavedChanges = true;
    }

    partial void OnVideoCodecChanged(VideoCodecOption value)
    {
        if (_isLoadingSettings) return;
        HasUnsavedChanges = true;
    }

    partial void OnVideoQualityChanged(VideoQualityOption value)
    {
        if (_isLoadingSettings) return;
        HasUnsavedChanges = true;
    }

    partial void OnHardwareAccelerationChanged(bool value)
    {
        if (_isLoadingSettings) return;
        HasUnsavedChanges = true;
        HardwareAccelerationChanged = true;
    }

    partial void OnApiEnabledChanged(bool value)
    {
        UpdateApiUrl();

        if (_isLoadingSettings) return;
        HasUnsavedChanges = true;
        ApiStatusChanged?.Invoke(this, new ApiStatusChangedEventArgs(ApiEnabled, ApiPort, IsApiRunning));
    }

    partial void OnApiPortChanged(int value)
    {
        UpdateApiUrl();

        if (_isLoadingSettings) return;
        HasUnsavedChanges = true;
        ApiStatusChanged?.Invoke(this, new ApiStatusChangedEventArgs(ApiEnabled, ApiPort, IsApiRunning));
    }

    partial void OnIsApiRunningChanged(bool value)
    {
        UpdateApiUrl();
        if (_isLoadingSettings) return;
        ApiStatusChanged?.Invoke(this, new ApiStatusChangedEventArgs(ApiEnabled, ApiPort, IsApiRunning));
    }

    private void ApplyTheme(string themeValue)
    {
        try
        {
            switch (themeValue)
            {
                case "Light":
                    while (_theme.ActiveBaseTheme.ToString() != "Light") _theme.SwitchBaseTheme();

                    break;
                case "Dark":
                    while (_theme.ActiveBaseTheme.ToString() != "Dark") _theme.SwitchBaseTheme();

                    break;
                case "Auto":
                    while (_theme.ActiveBaseTheme.ToString() != "Default") _theme.SwitchBaseTheme();

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
        SettingsChanged?.Invoke(this,
            new SettingsChangedEventArgs(DefaultRenderTimeoutSeconds, MaxRetryAttempts, VideoCodec.Value,
                VideoQuality.Value, Language.Value));

        await SaveSettingsToFileAsync();

        HasUnsavedChanges = false;
        HardwareAccelerationChanged = false;
    }

    [RelayCommand]
    private void OpenUrl(string urlPath)
    {
        if (string.IsNullOrEmpty(urlPath))
            return;

        // 构建完整的URL
        var fullUrl = ApiUrl + urlPath;
        UrlUtilities.OpenUrl(fullUrl);
    }

    [RelayCommand]
    private async Task CopyApiUrl()
    {
        if (string.IsNullOrEmpty(ApiUrl))
            return;

        try
        {
            // 使用新的Avalonia剪切板服务，传入当前ViewModel作为context
            var success = await ClipboardHelper.SetText(ApiUrl, this);
            Console.WriteLine(success
                ? $"[SettingsViewModel] ✅ API URL copied to clipboard: {ApiUrl}"
                : $"[SettingsViewModel] ❌ Failed to copy API URL to clipboard");

            // 显示toast提示
            if (success)
            {
                this.ShowSuccessToast(
                    Localizer.Localizer.Instance["ApiService_CopySuccess"],
                    Localizer.Localizer.Instance["ApiService_CopySuccessMessage"]);
            }
            else
            {
                this.ShowErrorToast(
                    Localizer.Localizer.Instance["ApiService_CopyFailed"],
                    Localizer.Localizer.Instance["ApiService_CopyFailedMessage"]);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Failed to copy API URL to clipboard: {ex.Message}");
            
            // 显示错误toast
            this.ShowErrorToast(
                Localizer.Localizer.Instance["ApiService_CopyFailed"],
                Localizer.Localizer.Instance["ApiService_CopyFailedMessage"]);
        }
    }


    public async Task SaveSettingsToFileAsync()
    {
        try
        {
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
            Console.WriteLine(
                success
                    ? $"[SettingsViewModel] ✅ Settings saved successfully - Selected Blender: {SelectedBlenderExecutable?.Path}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}, API: {ApiEnabled}@{ApiPort}"
                    : "[SettingsViewModel] ❌ Failed to save settings");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] ❌ Error saving settings: {ex.Message}");
        }
    }


    private async Task LoadSettingsFromFileAsync()
    {
        try
        {
            _isLoadingSettings = true;
            var settings = await _settingsPersistenceService.LoadSettingsAsync();

            if (settings.BlenderExecutables.Count != 0)
            {
                BlenderExecutables.Clear();

                var uniqueBlenders = settings.BlenderExecutables
                    .GroupBy(b => b.Path)
                    .Select(g => g.OrderByDescending(b => b.LastValidated).First())
                    .ToList();

                foreach (var blender in uniqueBlenders) BlenderExecutables.Add(blender);

                Console.WriteLine($"[SettingsViewModel] Loaded {BlenderExecutables.Count} unique Blender executables");

                if (!string.IsNullOrEmpty(settings.SelectedBlenderPath))
                {
                    SelectedBlenderExecutable =
                        BlenderExecutables.FirstOrDefault(b => b.Path == settings.SelectedBlenderPath);

                    Console.WriteLine(
                        $"[SettingsViewModel] Selected Blender: {SelectedBlenderExecutable?.Path ?? "NOT FOUND"}");
                }
            }

            if (settings.DefaultRenderTimeoutSeconds > 0)
                DefaultRenderTimeoutSeconds = settings.DefaultRenderTimeoutSeconds;

            if (settings.MaxRetryAttempts > 0) MaxRetryAttempts = settings.MaxRetryAttempts;

            if (!string.IsNullOrEmpty(settings.VideoCodec))
                VideoCodec = settings.VideoCodec switch
                {
                    "H264" => VideoCodecOption.H264,
                    "H265" => VideoCodecOption.H265,
                    "AV1" => VideoCodecOption.AV1,
                    _ => VideoCodecOption.H264
                };

            if (!string.IsNullOrEmpty(settings.VideoQuality))
                VideoQuality = settings.VideoQuality switch
                {
                    "LOSSLESS" => VideoQualityOption.Lossless,
                    "PERC_LOSSLESS" => VideoQualityOption.PerceptualLossless,
                    "HIGH" => VideoQualityOption.High,
                    "MEDIUM" => VideoQualityOption.Medium,
                    "LOW" => VideoQualityOption.Low,
                    _ => VideoQualityOption.PerceptualLossless
                };

            if (!string.IsNullOrEmpty(settings.Language))
            {
                var languageOption = LanguageOption.FindByValue(settings.Language);
                if (languageOption != null)
                {
                    Language = languageOption;
                    Localizer.Localizer.Instance.LoadLanguage(settings.Language);
                }
                else
                {
                    Language = LanguageOption.Default;
                    Localizer.Localizer.Instance.LoadLanguage(LanguageOption.Default.Value);
                }
            }
            else
            {
                Language = LanguageOption.Default;
                Localizer.Localizer.Instance.LoadLanguage(LanguageOption.Default.Value);
            }

            if (!string.IsNullOrEmpty(settings.BaseTheme))
            {
                var themeOption = ThemeOption.FindByValue(settings.BaseTheme);
                if (themeOption != null)
                {
                    // Set the attributes first, then apply the theme
                    BaseTheme = themeOption;
                    // Delay applying the theme to ensure the UI is updated
                    Dispatcher.UIThread.Post(() => { ApplyTheme(settings.BaseTheme); });
                }
            }

            HardwareAcceleration = settings.UseGpu;

            ApiEnabled = settings.ApiEnabled;
            ApiPort = settings.ApiPort;

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
        return [new FilePickerFileType("Executable") { Patterns = ["*.exe"] }];
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
public class SettingsChangedEventArgs(
    int defaultRenderTimeoutSeconds,
    int maxRetryAttempts,
    string videoCodec,
    string videoQuality,
    string language)
    : EventArgs
{
    public int DefaultRenderTimeoutSeconds { get; } = defaultRenderTimeoutSeconds;
    public int MaxRetryAttempts { get; } = maxRetryAttempts;
    public string VideoCodec { get; } = videoCodec;
    public string VideoQuality { get; } = videoQuality;
    public string Language { get; } = language;
}

// 初始化完成事件参数
public class InitializationCompletedEventArgs(bool isBlenderDetected) : EventArgs
{
    public bool IsBlenderDetected { get; } = isBlenderDetected;
}

// Blender验证状态变化事件参数
public class BlenderValidationChangedEventArgs(bool isValid, string message) : EventArgs
{
    public bool IsValid { get; } = isValid;
    public string Message { get; } = message;
}

// API状态变化事件参数
public class ApiStatusChangedEventArgs(bool isEnabled, int port, bool isRunning) : EventArgs
{
    public bool IsEnabled { get; } = isEnabled;
    public int Port { get; } = port;
    public bool IsRunning { get; } = isRunning;
}