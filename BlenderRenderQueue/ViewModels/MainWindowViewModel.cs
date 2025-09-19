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
		// 使用新的渲染队列视图模型替代测试视图模型
		Content = new MainRenderViewModel();
	}

	[RelayCommand]
	private void OpenSettings()
	{
		var settingsViewModel = new SettingsViewModel();
		
		// 订阅设置变化事件
		settingsViewModel.SettingsChanged += OnSettingsChanged;
		
		DialogManager.CreateDialog()
			.WithTitle("设置")
			.WithContent(settingsViewModel)
			.WithActionButton("保存", _ => 
			{
				settingsViewModel.SaveSettingsCommand.Execute(null);
			}, true)
			.WithActionButton("取消", _ => { }, true)
			.Dismiss().ByClickingBackground()
			.TryShow();
	}

	private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
	{
		// 将设置应用到主渲染视图模型
		if (Content is MainRenderViewModel mainRender)
		{
			mainRender.BlenderPath = e.BlenderPath;
			mainRender.FfmpegPath = e.FfmpegPath;
		}
	}
}