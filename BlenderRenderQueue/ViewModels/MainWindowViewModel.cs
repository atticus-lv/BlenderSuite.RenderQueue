using CommunityToolkit.Mvvm.Input;
using SukiUI.Controls;
using SukiUI.Dialogs;

namespace BlenderRenderQueue.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
	public ViewModelBase Content { get; }
	public ISukiDialogManager DialogManager { get; } = new SukiDialogManager();

	public MainWindowViewModel()
	{
		// 使用新的渲染队列视图模型
		Content = new MainRenderViewModel();
	}

	[RelayCommand]
	private void OpenSettings()
	{
		// 委托给 MainRenderViewModel 处理
		if (Content is MainRenderViewModel mainRender)
		{
			mainRender.OpenSettingsCommand.Execute(null);
		}
	}
}