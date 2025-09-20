using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.BlenderService;
using CommunityToolkit.Mvvm.Input;

namespace BlenderRenderQueue.ViewModels;

/// <summary>
/// Blender文件属性展示的ViewModel
/// </summary>
public partial class BlendScenePropertiesViewModel : ViewModelBase
{
    [ObservableProperty]
    private BlendSceneProperties _sceneProperties = new();

    [ObservableProperty]
    private Dictionary<string, BlendSceneProperties> _allScenes = new();

    partial void OnAllScenesChanged(Dictionary<string, BlendSceneProperties> value)
    {
        // 更新场景名称列表
        SceneNames = value?.Keys.ToList() ?? new List<string>();
        
        // 当场景数据改变时，更新当前激活场景的属性
        _activeSceneProperties = ActiveSceneProperties;
        SceneProperties = _activeSceneProperties;
        
        // 触发相关属性通知
        OnPropertyChanged(nameof(ActiveSceneProperties));
        OnPropertyChanged(nameof(CanOpenFramePathDirectory));
        OnPropertyChanged(nameof(ShowEmptyState));
        
        Console.WriteLine($"[BlendScenePropertiesViewModel] AllScenes changed, count: {value?.Count ?? 0}");
    }

    [ObservableProperty]
    private string _activeSceneName = string.Empty;

    [ObservableProperty]
    private string _defaultSceneName = string.Empty;
    
    public bool IsNotDefaultScene => ActiveSceneName != DefaultSceneName;
    
    partial void OnActiveSceneNameChanged(string value)
    {
        Console.WriteLine($"[BlendScenePropertiesViewModel] ActiveSceneName changing from '{ActiveSceneName}' to '{value}'");
        
        // 更新当前激活场景的属性
        _activeSceneProperties = ActiveSceneProperties;
        
        // 当激活场景改变时，更新SceneProperties以保持向后兼容
        SceneProperties = _activeSceneProperties;
        
        // 触发所有相关属性的通知
        OnPropertyChanged(nameof(ActiveSceneProperties));
        OnPropertyChanged(nameof(CanOpenFramePathDirectory));
        OnPropertyChanged(nameof(ShowEmptyState));
        
        Console.WriteLine($"[BlendScenePropertiesViewModel] ActiveSceneName changed to: {value}, ActiveSceneProperties.IsLoaded: {ActiveSceneProperties.IsLoaded}");
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// 是否有错误信息
    /// </summary>
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    private BlendSceneProperties _activeSceneProperties = new();

    /// <summary>
    /// 获取当前激活场景的属性
    /// </summary>
    public BlendSceneProperties ActiveSceneProperties
    {
        get
        {
            if (string.IsNullOrEmpty(ActiveSceneName) || !AllScenes.ContainsKey(ActiveSceneName))
            {
                return new BlendSceneProperties();
            }

            return AllScenes[ActiveSceneName];
        }
    }

    [ObservableProperty]
    private List<string> _sceneNames = new();

    /// <summary>
    /// 是否显示空状态（文件未加载且不在加载中）
    /// </summary>
    public bool ShowEmptyState
    {
        get
        {
            var result = !ActiveSceneProperties.IsLoaded && !IsLoading;
            Console.WriteLine(
                $"[BlendScenePropertiesViewModel] ShowEmptyState calculated: {result} (IsLoaded: {ActiveSceneProperties.IsLoaded}, IsLoading: {IsLoading})");
            return result;
        }
    }

    /// <summary>
    /// 加载文件属性
    /// </summary>
    public async Task LoadPropertiesAsync(BasePythonProcessService process, string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(blendFilePath))
        {
            ErrorMessage = "文件路径不能为空";
            return;
        }

        Console.WriteLine(
            $"[BlendScenePropertiesViewModel] Starting LoadPropertiesAsync for: {Path.GetFileName(blendFilePath)}");
        Console.WriteLine(
            $"[BlendScenePropertiesViewModel] Initial state - IsLoading: {IsLoading}, IsLoaded: {ActiveSceneProperties.IsLoaded}, ShowEmptyState: {ShowEmptyState}");

        IsLoading = true;
        ErrorMessage = string.Empty;
        LoadingMessage = "正在加载文件属性...";

        Console.WriteLine(
            $"[BlendScenePropertiesViewModel] After setting IsLoading=true - IsLoading: {IsLoading}, ShowEmptyState: {ShowEmptyState}");

        try
        {
            var queryService = new BlenderQueryService();

            // 一次性查询所有文件属性
            LoadingMessage = "正在获取文件属性...";
            var (activeScene, sceneData) =
                await queryService.GetAllFilePropertiesAsync(process, blendFilePath, cancellationToken);

            AllScenes = sceneData;
            ActiveSceneName = activeScene;
            DefaultSceneName = ActiveSceneName;
            SceneProperties = ActiveSceneProperties; // 保持向后兼容
            LoadingMessage = "加载完成";

            Console.WriteLine(
                $"[BlendScenePropertiesViewModel] Properties loaded successfully - ActiveScene: {ActiveSceneName}, ScenesCount: {AllScenes.Count}, IsLoaded: {ActiveSceneProperties.IsLoaded}");

            // 通知UI更新计算属性
            OnPropertyChanged(nameof(HasErrorMessage));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ActiveSceneProperties));
            OnPropertyChanged(nameof(SceneNames));

            Console.WriteLine(
                $"[BlendScenePropertiesViewModel] After loading - IsLoading: {IsLoading}, IsLoaded: {ActiveSceneProperties.IsLoaded}, ShowEmptyState: {ShowEmptyState}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加载文件属性失败: {ex.Message}";
            OnPropertyChanged(nameof(HasErrorMessage));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
            Console.WriteLine(
                $"[BlendScenePropertiesViewModel] Finally block - IsLoading: {IsLoading}, IsLoaded: {ActiveSceneProperties.IsLoaded}, ShowEmptyState: {ShowEmptyState}");
        }
    }

    /// <summary>
    /// 清空属性
    /// </summary>
    public void ClearProperties()
    {
        SceneProperties = new BlendSceneProperties();
        AllScenes = new Dictionary<string, BlendSceneProperties>();
        ActiveSceneName = string.Empty;
        SceneNames = new List<string>();
        ErrorMessage = string.Empty;
        LoadingMessage = string.Empty;
        OnPropertyChanged(nameof(HasErrorMessage));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ActiveSceneProperties));
    }

    /// <summary>
    /// 打开帧路径所在的文件夹
    /// </summary>
    [RelayCommand]
    private void OpenFramePathDirectory()
    {
        try
        {
            if (string.IsNullOrEmpty(ActiveSceneProperties.FramePath))
            {
                ErrorMessage = "帧路径为空，无法打开文件夹";
                OnPropertyChanged(nameof(HasErrorMessage));
                return;
            }

            // 获取帧路径所在的目录
            var framePathDirectory = Path.GetDirectoryName(ActiveSceneProperties.FramePath);

            if (string.IsNullOrEmpty(framePathDirectory))
            {
                ErrorMessage = "无法获取帧路径的目录";
                OnPropertyChanged(nameof(HasErrorMessage));
                return;
            }

            if (!Directory.Exists(framePathDirectory))
            {
                ErrorMessage = $"帧路径目录不存在: {framePathDirectory}";
                OnPropertyChanged(nameof(HasErrorMessage));
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{framePathDirectory.Replace('/', '\\')}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"打开帧路径文件夹失败: {ex.Message}";
            OnPropertyChanged(nameof(HasErrorMessage));
        }
    }

    /// <summary>
    /// 是否可以打开帧路径文件夹
    /// </summary>
    public bool CanOpenFramePathDirectory => !string.IsNullOrEmpty(ActiveSceneProperties.FramePath) &&
                                             !string.IsNullOrEmpty(
                                                 Path.GetDirectoryName(ActiveSceneProperties.FramePath));
}