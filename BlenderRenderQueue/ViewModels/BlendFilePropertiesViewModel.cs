using System;
using System.Diagnostics;
using System.IO;
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
public partial class BlendFilePropertiesViewModel : ViewModelBase
{
	[ObservableProperty]
	private BlendFileSceneProperties _sceneProperties = new();

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

	/// <summary>
	/// 是否显示空状态（文件未加载且不在加载中）
	/// </summary>
	public bool ShowEmptyState
	{
		get
		{
			var result = !SceneProperties.IsLoaded && !IsLoading;
			Console.WriteLine($"[BlendFilePropertiesViewModel] ShowEmptyState calculated: {result} (IsLoaded: {SceneProperties.IsLoaded}, IsLoading: {IsLoading})");
			return result;
		}
	}

	/// <summary>
	/// 加载文件属性
	/// </summary>
	public async Task LoadPropertiesAsync(BasePythonProcessService process, string blendFilePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(blendFilePath))
		{
			ErrorMessage = "文件路径不能为空";
			return;
		}

		Console.WriteLine($"[BlendFilePropertiesViewModel] Starting LoadPropertiesAsync for: {Path.GetFileName(blendFilePath)}");
		Console.WriteLine($"[BlendFilePropertiesViewModel] Initial state - IsLoading: {IsLoading}, IsLoaded: {SceneProperties.IsLoaded}, ShowEmptyState: {ShowEmptyState}");
		
		IsLoading = true;
		ErrorMessage = string.Empty;
		LoadingMessage = "正在加载文件属性...";
		
		Console.WriteLine($"[BlendFilePropertiesViewModel] After setting IsLoading=true - IsLoading: {IsLoading}, ShowEmptyState: {ShowEmptyState}");

		try
		{
			var queryService = new BlenderQueryService();
			
			// 一次性查询所有文件属性
			LoadingMessage = "正在获取文件属性...";
			var properties = await queryService.GetAllFilePropertiesAsync(process, blendFilePath, cancellationToken);

			SceneProperties = properties;
			LoadingMessage = "加载完成";
			
			Console.WriteLine($"[BlendFilePropertiesViewModel] Properties loaded successfully - IsLoaded: {SceneProperties.IsLoaded}");
			
			// 通知UI更新计算属性
			OnPropertyChanged(nameof(HasErrorMessage));
			OnPropertyChanged(nameof(ShowEmptyState));
			
			Console.WriteLine($"[BlendFilePropertiesViewModel] After loading - IsLoading: {IsLoading}, IsLoaded: {SceneProperties.IsLoaded}, ShowEmptyState: {ShowEmptyState}");
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
			Console.WriteLine($"[BlendFilePropertiesViewModel] Finally block - IsLoading: {IsLoading}, IsLoaded: {SceneProperties.IsLoaded}, ShowEmptyState: {ShowEmptyState}");
		}
	}

	/// <summary>
	/// 清空属性
	/// </summary>
	public void ClearProperties()
	{
		SceneProperties = new BlendFileSceneProperties();
		ErrorMessage = string.Empty;
		LoadingMessage = string.Empty;
		OnPropertyChanged(nameof(HasErrorMessage));
		OnPropertyChanged(nameof(ShowEmptyState));
	}

	/// <summary>
	/// 打开帧路径所在的文件夹
	/// </summary>
	[RelayCommand]
	public void OpenFramePathDirectory()
	{
		try
		{
			if (string.IsNullOrEmpty(SceneProperties.FramePath))
			{
				ErrorMessage = "帧路径为空，无法打开文件夹";
				OnPropertyChanged(nameof(HasErrorMessage));
				return;
			}

			// 获取帧路径所在的目录
			var framePathDirectory = Path.GetDirectoryName(SceneProperties.FramePath);
			
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
	public bool CanOpenFramePathDirectory => !string.IsNullOrEmpty(SceneProperties.FramePath) && 
	                                         !string.IsNullOrEmpty(Path.GetDirectoryName(SceneProperties.FramePath));
}
