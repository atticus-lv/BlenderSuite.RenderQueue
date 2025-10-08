using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Services.Business.Blender;
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
        OnPropertyChanged(nameof(SortedSceneNames));
        
        Console.WriteLine($"[BlendScenePropertiesViewModel] AllScenes changed, count: {value?.Count ?? 0}");
    }

    [ObservableProperty]
    private string _activeSceneName = string.Empty;

    [ObservableProperty]
    private string _defaultSceneName = string.Empty;

    partial void OnDefaultSceneNameChanged(string value)
    {
        // 当默认场景名称改变时，触发IsNotDefaultScene的通知
        OnPropertyChanged(nameof(IsNotDefaultScene));
        OnPropertyChanged(nameof(SortedSceneNames));
        Console.WriteLine($"[BlendScenePropertiesViewModel] DefaultSceneName changed to: {value}");
    }

    partial void OnActiveSceneNameChanged(string value)
    {
        // Console.WriteLine($"[BlendScenePropertiesViewModel] ActiveSceneName changing from '{ActiveSceneName}' to '{value}'");
        
        // 更新当前激活场景的属性
        _activeSceneProperties = ActiveSceneProperties;
        
        // 当激活场景改变时，更新SceneProperties以保持向后兼容
        SceneProperties = _activeSceneProperties;
        
        // 触发所有相关属性的通知
        OnPropertyChanged(nameof(ActiveSceneProperties));
        OnPropertyChanged(nameof(CanOpenFramePathDirectory));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(IsNotDefaultScene));
        
        // Console.WriteLine($"[BlendScenePropertiesViewModel] ActiveSceneName changed to: {value}, ActiveSceneProperties.IsLoaded: {ActiveSceneProperties.IsLoaded}");
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
    /// 排序后的场景名称列表，默认场景排在第一个
    /// </summary>
    public List<string> SortedSceneNames
    {
        get
        {
            if (SceneNames == null || !SceneNames.Any())
                return new List<string>();

            var sortedList = new List<string>(SceneNames);
            
            // 如果有默认场景且不在第一位，将其移到第一位
            if (!string.IsNullOrEmpty(DefaultSceneName) && sortedList.Contains(DefaultSceneName))
            {
                sortedList.Remove(DefaultSceneName);
                sortedList.Insert(0, DefaultSceneName);
            }
            
            return sortedList;
        }
    }

    /// <summary>
    /// 当前场景是否不是默认场景
    /// </summary>
    public bool IsNotDefaultScene => !string.IsNullOrEmpty(ActiveSceneName) && 
                                     !string.IsNullOrEmpty(DefaultSceneName) && 
                                     ActiveSceneName != DefaultSceneName;

    /// <summary>
    /// 是否显示空状态（文件未加载且不在加载中）
    /// </summary>
    public bool ShowEmptyState
    {
        get
        {
            var result = !ActiveSceneProperties.IsLoaded && !IsLoading;
            return result;
        }
    }

    /// <summary>
    /// 加载文件属性（使用临时进程，查询完成后自动释放）
    /// </summary>
    public async Task LoadPropertiesAsync(string blenderPath, string blendFilePath,
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
        LoadingMessage = "SceneProperties_LoadingFileProperties";

        Console.WriteLine(
            $"[BlendScenePropertiesViewModel] After setting IsLoading=true - IsLoading: {IsLoading}, ShowEmptyState: {ShowEmptyState}");

        try
        {
            var queryService = new BlenderQueryService();

            // 一次性查询所有文件属性（使用临时进程）
            LoadingMessage = "SceneProperties_GettingFileProperties";
            var (activeScene, sceneData) =
                await queryService.GetAllFilePropertiesWithTempProcessAsync(blenderPath, blendFilePath, cancellationToken);

            AllScenes = sceneData;
            ActiveSceneName = activeScene;
            DefaultSceneName = ActiveSceneName;
            SceneProperties = ActiveSceneProperties; // 保持向后兼容
            LoadingMessage = "SceneProperties_LoadingComplete";

            Console.WriteLine(
                $"[BlendScenePropertiesViewModel] Properties loaded successfully - ActiveScene: {ActiveSceneName}, ScenesCount: {AllScenes.Count}, IsLoaded: {ActiveSceneProperties.IsLoaded}");

            // 通知UI更新计算属性
            OnPropertyChanged(nameof(HasErrorMessage));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ActiveSceneProperties));
            OnPropertyChanged(nameof(SceneNames));

            // Console.WriteLine(
            //     $"[BlendScenePropertiesViewModel] After loading - IsLoading: {IsLoading}, IsLoaded: {ActiveSceneProperties.IsLoaded}, ShowEmptyState: {ShowEmptyState}");
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
        if (string.IsNullOrEmpty(ActiveSceneProperties.FramePath))
        {
            ErrorMessage = "帧路径为空，无法打开文件夹";
            OnPropertyChanged(nameof(HasErrorMessage));
            return;
        }

        var success = FileSystemHelper.OpenFileDirectory(ActiveSceneProperties.FramePath);
        if (!success)
        {
            ErrorMessage = "打开帧路径文件夹失败";
            OnPropertyChanged(nameof(HasErrorMessage));
        }
        else
        {
            // 清除之前的错误信息
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = string.Empty;
                OnPropertyChanged(nameof(HasErrorMessage));
            }
        }
    }

    /// <summary>
    /// 是否可以打开帧路径文件夹
    /// </summary>
    public bool CanOpenFramePathDirectory => !string.IsNullOrEmpty(ActiveSceneProperties.FramePath) && 
                                             FileSystemHelper.CanOpenFileDirectory(ActiveSceneProperties.FramePath);
}