using System;
using System.Collections.Generic;
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
    private readonly IBlenderQueryService _queryService;

    public BlendScenePropertiesViewModel(IBlenderQueryService queryService)
    {
        _queryService = queryService;
    }

    [ObservableProperty] private BlendSceneProperties _sceneProperties = new();

    [ObservableProperty] private Dictionary<string, BlendSceneProperties> _allScenes = new();

    partial void OnAllScenesChanged(Dictionary<string, BlendSceneProperties> value)
    {
        // 更新场景名称列表
        SceneNames = value.Keys.ToList();

        // 当场景数据改变时，更新当前激活场景的属性
        SceneProperties = SelectedSceneProperties;

        // 触发相关属性通知
        OnPropertyChanged(nameof(SelectedSceneProperties));
        OnPropertyChanged(nameof(SelectedSceneName));
        OnPropertyChanged(nameof(CanOpenFramePathDirectory));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(SortedScenes));
    }

    [ObservableProperty] private BlendSceneProperties _selectedScene = new();

    partial void OnSelectedSceneChanged(BlendSceneProperties value)
    {
        // Console.WriteLine($"[BlendScenePropertiesViewModel] SelectedScene changing");

        // 当选择的场景改变时，更新SceneProperties以保持向后兼容
        SceneProperties = value;

        // 触发所有相关属性的通知
        OnPropertyChanged(nameof(SelectedSceneProperties));
        OnPropertyChanged(nameof(CanOpenFramePathDirectory));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(SelectedSceneName));

        // Console.WriteLine($"[BlendScenePropertiesViewModel] SelectedScene changed, IsLoaded: {value?.IsLoaded}");
    }

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _loadingMessage = string.Empty;

    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>
    /// 是否有错误信息
    /// </summary>
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// 获取当前选择的场景属性（向后兼容）
    /// </summary>
    public BlendSceneProperties SelectedSceneProperties => SelectedScene;

    /// <summary>
    /// 获取当前选择的场景名称（向后兼容）
    /// </summary>
    public string? SelectedSceneName => SelectedScene.SceneName;

    [ObservableProperty] private List<string> _sceneNames = new();

    /// <summary>
    /// 排序后的场景属性集合，默认场景排在第一个
    /// </summary>
    public List<BlendSceneProperties> SortedScenes
    {
        get
        {
            if (AllScenes.Count == 0)
                return [];

            return AllScenes
                .OrderByDescending(scene => scene.Value.IsDefaultScene)
                .ThenBy(scene => scene.Key)
                .Select(scene => scene.Value)
                .ToList();
        }
    }


    public bool ShowEmptyState => !SelectedSceneProperties.IsLoaded && !IsLoading;


    public async Task LoadPropertiesAsync(string blenderPath, string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(blendFilePath))
        {
            ErrorMessage = "文件路径不能为空";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        LoadingMessage = "SceneProperties_LoadingFileProperties";

        try
        {
            // Query all file properties at once (using a temporary process)
            LoadingMessage = "SceneProperties_GettingFileProperties";
            var (_, sceneData) =
                await _queryService.GetAllFilePropertiesWithTempProcessAsync(blenderPath, blendFilePath, cancellationToken);

            AllScenes = sceneData;

            // Set the default selected scene (default scene)
            var defaultScene = sceneData.Values.FirstOrDefault(scene => scene.IsDefaultScene);
            if (defaultScene != null)
            {
                SelectedScene = defaultScene;
            }
            else
            {
                SelectedScene = sceneData.Values.FirstOrDefault() ?? new BlendSceneProperties();
            }

            SceneProperties = SelectedSceneProperties; // 保持向后兼容
            LoadingMessage = "SceneProperties_LoadingComplete";

            OnPropertyChanged(nameof(HasErrorMessage));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(SelectedSceneProperties));
            OnPropertyChanged(nameof(SelectedSceneName));
            OnPropertyChanged(nameof(SceneNames));
            OnPropertyChanged(nameof(SortedScenes));
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
        }
    }


    [RelayCommand]
    private void OpenFramePathDirectory()
    {
        if (string.IsNullOrEmpty(SelectedSceneProperties.FramePath))
        {
            ErrorMessage = "帧路径为空，无法打开文件夹";
            OnPropertyChanged(nameof(HasErrorMessage));
            return;
        }

        var success = FileSystemHelper.OpenFileDirectory(SelectedSceneProperties.FramePath);
        if (!success)
        {
            ErrorMessage = "打开帧路径文件夹失败";
        }
        else
        {
            // 清除之前的错误信息
            if (string.IsNullOrEmpty(ErrorMessage)) return;
            ErrorMessage = string.Empty;
        }

        OnPropertyChanged(nameof(HasErrorMessage));
    }


    public bool CanOpenFramePathDirectory => !string.IsNullOrEmpty(SelectedSceneProperties.FramePath) &&
                                             FileSystemHelper.CanOpenFileDirectory(SelectedSceneProperties.FramePath);
}
