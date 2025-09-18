using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.BlenderService;

namespace BlenderRenderQueue.ViewModels;

/// <summary>
/// Blender文件属性展示的ViewModel
/// </summary>
public partial class BlendFilePropertiesViewModel : ViewModelBase
{
	[ObservableProperty]
	private BlendFileProperties _properties = new();

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
	/// 加载文件属性
	/// </summary>
	public async Task LoadPropertiesAsync(BasePythonProcessService process, string blendFilePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(blendFilePath))
		{
			ErrorMessage = "文件路径不能为空";
			return;
		}

		IsLoading = true;
		ErrorMessage = string.Empty;
		LoadingMessage = "正在加载文件属性...";

		try
		{
			var queryService = new BlenderQueryService();
			var properties = new BlendFileProperties
			{
				FilePath = blendFilePath
			};

			// 查询场景帧范围
			LoadingMessage = "正在获取场景帧范围...";
			var (frameStart, frameEnd) = await queryService.GetSceneFramesAsync(process, blendFilePath, cancellationToken);
			properties.FrameStart = frameStart;
			properties.FrameEnd = frameEnd;

			// 查询相机名称
			LoadingMessage = "正在获取相机信息...";
			properties.CameraName = await queryService.GetSceneCameraAsync(process, blendFilePath, cancellationToken);

			// 查询渲染输出路径
			LoadingMessage = "正在获取渲染输出路径...";
			properties.RenderOutputPath = await queryService.GetRenderOutputPathAsync(process, blendFilePath, cancellationToken);

			// 查询渲染输出格式
			LoadingMessage = "正在获取渲染输出格式...";
			properties.RenderOutputFormat = await queryService.GetRenderOutputFormatAsync(process, blendFilePath, cancellationToken);

			Properties = properties;
			LoadingMessage = "加载完成";
			
			// 通知UI更新计算属性
			OnPropertyChanged(nameof(HasErrorMessage));
		}
		catch (Exception ex)
		{
			ErrorMessage = $"加载文件属性失败: {ex.Message}";
			OnPropertyChanged(nameof(HasErrorMessage));
		}
		finally
		{
			IsLoading = false;
		}
	}

	/// <summary>
	/// 清空属性
	/// </summary>
	public void ClearProperties()
	{
		Properties = new BlendFileProperties();
		ErrorMessage = string.Empty;
		LoadingMessage = string.Empty;
		OnPropertyChanged(nameof(HasErrorMessage));
	}
}
