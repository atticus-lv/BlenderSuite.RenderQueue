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
			
			// 一次性查询所有文件属性
			LoadingMessage = "正在获取文件属性...";
			var properties = await queryService.GetAllFilePropertiesAsync(process, blendFilePath, cancellationToken);

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
