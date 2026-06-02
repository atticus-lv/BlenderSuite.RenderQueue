using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BlenderRenderQueue.Extensions;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI;

namespace BlenderRenderQueue.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private const string BlenderValidationChannel = nameof(SettingsViewModel);

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

    private readonly SukiTheme _theme;


    /// <summary>
    ///     更新队列状态（与开始队列按钮逻辑保持一致）
    /// </summary>
    public void UpdateQueueState(QueueState queueState)
    {
        // 只有在队列空闲或完成时才允许切换Blender
        CanSwitchBlender = queueState == QueueState.Idle || queueState == QueueState.Completed;
        _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"更新队列状态 - QueueState: {queueState}, CanSwitchBlender: {CanSwitchBlender}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());
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
        _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"SelectedBlenderExecutable changed: {value?.Path ?? "NULL"}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());

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
    private readonly ISettingsPersistenceService _settingsPersistenceService;
    private readonly IBlenderValidationService _blenderValidationService;
    private readonly IRenderLogService _logService;
    private bool _isLoadingSettings;

    // 事件：当设置发生变化时通知
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    // 事件：当初始化完成时通知
    public event EventHandler<InitializationCompletedEventArgs>? InitializationCompleted;

    // 事件：当Blender验证状态发生变化时通知
    public event EventHandler<BlenderValidationChangedEventArgs>? BlenderValidationChanged;

    public SettingsViewModel(
        ISettingsPersistenceService settingsPersistenceService,
        IBlenderValidationService blenderValidationService,
        IRenderLogService logService)
    {
        // 构造函数中不进行自动检测，等待StartInitialization调用
        _settingsPersistenceService = settingsPersistenceService;
        _blenderValidationService = blenderValidationService;
        _logService = logService;
        _theme = new SukiTheme();

        // 订阅主题变化事件
        _theme.OnBaseThemeChanged += variant =>
        {
            var themeValue = variant.ToString();
            var themeOption = ThemeOption.FindByValue(themeValue);
            if (themeOption != null) BaseTheme = themeOption;

            // 可以在这里添加Toast通知
            _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"Theme changed to: {variant}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());
        };

        // 初始化当前主题
        var currentThemeValue = _theme.ActiveBaseTheme.ToString();
        var currentThemeOption = ThemeOption.FindByValue(currentThemeValue);
        if (currentThemeOption != null) BaseTheme = currentThemeOption;
    }

    public void StartInitialization()
    {
        // 开始初始化检测
        Task.Run(InitializeAsync).FireAndForget(
            source: nameof(SettingsViewModel),
            message: "设置初始化后台任务失败。");
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
            _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Error during initialization: {ex.Message}", source: "SettingsViewModel");
        }

        // 通知初始化完成
        Dispatcher.UIThread.Post(() =>
        {
            InitializationCompleted?.Invoke(this, new InitializationCompletedEventArgs(blenderDetected));
        });
    }

    private void ValidateSelectedBlender(BlenderExecutable blender)
    {
        var request = _blenderValidationService.BeginValidation(blender.Path, BlenderValidationChannel);

        // 重置验证状态
        HasBlenderValidationError = false;
        BlenderValidationMessage = string.Empty;

        var preconditionResult = _blenderValidationService.ValidatePreconditions(request);
        if (preconditionResult != null)
        {
            HasBlenderValidationError = true;
            BlenderValidationMessage = preconditionResult.Status switch
            {
                BlenderValidationStatus.EmptyPath => "Blender路径为空",
                BlenderValidationStatus.FileNotFound => "指定的文件不存在",
                _ => preconditionResult.Message
            };
            NotifyBlenderValidationChanged();
            return;
        }

        // 异步获取Blender版本信息
        LoadBlenderInfoAsync(blender, request).FireAndForget(
            _logService,
            source: nameof(SettingsViewModel),
            message: "设置页后台加载 Blender 信息失败。");
    }


    /// <summary>
    ///     验证单个Blender（用于自动检测后的验证）
    /// </summary>
    private async Task ValidateBlenderAsync(BlenderExecutable blender)
    {
        var result = await _blenderValidationService.ValidatePathAsync(blender.Path);

        // 更新UI线程上的属性
        Dispatcher.UIThread.Post(() =>
        {
            if (result.Status == BlenderValidationStatus.Success && result.VersionInfo != null)
            {
                blender.UpdateFromVersionInfo(result.VersionInfo);
                blender.UpdateValidationStatus(true, DateTime.UtcNow);

                // 触发集合更改通知，让UI更新
                var successIndex = BlenderExecutables.IndexOf(blender);
                if (successIndex >= 0) BlenderExecutables[successIndex] = blender;

                _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"✅ Auto-validated Blender: {blender.Path} - {blender.Version}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());
                return;
            }

            blender.UpdateValidationStatus(false, DateTime.UtcNow);

            // 触发集合更改通知，让UI更新
            var index = BlenderExecutables.IndexOf(blender);
            if (index >= 0) BlenderExecutables[index] = blender;

            _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Auto-validation failed for Blender: {blender.Path} - {result.Message}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());
        });
    }

    private async Task LoadBlenderInfoAsync(BlenderExecutable blender, BlenderValidationRequest request)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsValidationRequestCurrent(blender, request))
            {
                return;
            }

            IsLoadingBlenderInfo = true;
        });

        if (!IsValidationRequestCurrent(blender, request))
        {
            return;
        }

        var result = await _blenderValidationService.ValidateAsync(request);

        if (!IsValidationRequestCurrent(blender, request) || result.IsCanceled || result.Status == BlenderValidationStatus.Stale)
        {
            return;
        }

        // 更新UI线程上的属性
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsValidationRequestCurrent(blender, request))
            {
                return;
            }

            if (result.Status == BlenderValidationStatus.Success && result.VersionInfo != null)
            {
                // 更新Blender信息
                blender.UpdateFromVersionInfo(result.VersionInfo);
                blender.UpdateValidationStatus(true, DateTime.UtcNow);

                // 触发集合更改通知，让UI更新
                var index = BlenderExecutables.IndexOf(blender);
                if (index >= 0) BlenderExecutables[index] = blender;

                IsLoadingBlenderInfo = false;
                HasBlenderValidationError = false;
                BlenderValidationMessage = string.Empty;
                NotifyBlenderValidationChanged();
                return;
            }

            IsLoadingBlenderInfo = false;
            blender.UpdateValidationStatus(false, DateTime.UtcNow);
            HasBlenderValidationError = true;
            BlenderValidationMessage = result.Status switch
            {
                BlenderValidationStatus.EmptyPath => "Blender路径为空",
                BlenderValidationStatus.FileNotFound => "指定的文件不存在",
                _ => $"Blender验证失败: {result.Message}"
            };
            NotifyBlenderValidationChanged();
        });
    }

    private bool IsValidationRequestCurrent(BlenderExecutable blender, BlenderValidationRequest request)
    {
        return _blenderValidationService.IsCurrent(request) &&
               SelectedBlenderExecutable != null &&
               string.Equals(SelectedBlenderExecutable.Path, blender.Path, StringComparison.Ordinal);
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
            var detectedBlenders = new List<string>();

            if (BlenderLocator.TryFindBlenderExe(out var exe))
            {
                detectedBlenders.Add(exe);
            }

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
                        Task.Run(() => ValidateBlenderAsync(blender)).FireAndForget(
                            source: nameof(SettingsViewModel),
                            message: "自动检测 Blender 后台验证任务失败。");
                    }

                    // AutoSelect is automatically selected only if autoSelect is true and there is currently no Blender selected
                    if (autoSelect && SelectedBlenderExecutable == null && BlenderExecutables.Any())
                        SelectedBlenderExecutable = BlenderExecutables.First();
                });
                return Task.FromResult(true);
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
            Task.Run(SaveSettingsToFileAsync).FireAndForget(
                source: nameof(SettingsViewModel),
                message: "重启前后台保存设置任务失败。");
            var success = FileSystemHelper.RestartApplication();
            _logService.Write(
                success ? RenderLogLevel.Info : RenderLogLevel.Error,
                RenderLogScope.System,
                success ? "应用重启已发起。" : "应用重启失败。",
                source: "SettingsViewModel");
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Error restarting application: {ex.Message}", source: "SettingsViewModel");
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

            _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"Applied theme: {themeValue}, Current: {_theme.ActiveBaseTheme}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error applying theme: {ex.Message}", source: "SettingsViewModel");
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
                UseGpu = HardwareAcceleration
            };

            var success = await _settingsPersistenceService.SaveSettingsAsync(settings);
            _logService.Write(
                success ? RenderLogLevel.Info : RenderLogLevel.Error,
                RenderLogScope.System,
                success
                    ? $"设置保存完成，默认超时: {DefaultRenderTimeoutSeconds}s，最大重试: {MaxRetryAttempts}"
                    : "设置保存失败。",
                source: "SettingsViewModel");
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Error saving settings: {ex.Message}", source: "SettingsViewModel");
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

                _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"Loaded {BlenderExecutables.Count} unique Blender executables", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());

                if (!string.IsNullOrEmpty(settings.SelectedBlenderPath))
                {
                    SelectedBlenderExecutable =
                        BlenderExecutables.FirstOrDefault(b => b.Path == settings.SelectedBlenderPath);

                    _logService.Write(RenderLogLevel.Warning, RenderLogScope.System, $"Selected Blender: {SelectedBlenderExecutable?.Path ?? "NOT FOUND"}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());
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

            _logService.Write(RenderLogLevel.Warning, RenderLogScope.System, $"✅ Settings loaded successfully - Selected Blender: {SelectedBlenderExecutable?.Path}, Timeout: {DefaultRenderTimeoutSeconds}s, MaxRetry: {MaxRetryAttempts}", source: "SettingsViewModel", metadata: RenderLogMetadata.Diagnostic());
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Error loading settings: {ex.Message}", source: "SettingsViewModel");
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private static IEnumerable<FilePickerFileType> GetBlenderExecutableFileTypes()
    {
        if (OperatingSystem.IsWindows())
        {
            return [new FilePickerFileType("Executable") { Patterns = ["*.exe"] }];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                new FilePickerFileType("Blender")
                {
                    Patterns = ["Blender", "*Blender*"],
                    AppleUniformTypeIdentifiers = ["public.unix-executable"]
                }
            ];
        }

        return [new FilePickerFileType("Executable") { Patterns = ["*"] }];
    }


    public void Dispose()
    {
        _blenderValidationService.CancelCurrent(BlenderValidationChannel);
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
