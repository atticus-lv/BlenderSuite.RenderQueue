using CommunityToolkit.Mvvm.Input;
using SukiUI.Controls;
using SukiUI.Dialogs;

namespace BlenderRenderQueue.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
	public ViewModelBase Content { get; }
	public ISukiDialogManager DialogManager { get; } = new SukiDialogManager();
	private SettingsViewModel? _settingsViewModel;

	public MainWindowViewModel()
	{
		// 使用新的渲染队列视图模型替代测试视图模型
		Content = new MainRenderViewModel();
		
		// 初始化设置并检测路径
		InitializeSettings();
	}

	private void InitializeSettings()
	{
		_settingsViewModel = new SettingsViewModel();
		
		// 订阅设置变化事件
		_settingsViewModel.SettingsChanged += OnSettingsChanged;
		_settingsViewModel.InitializationCompleted += OnInitializationCompleted;
		
		// 开始初始化检测
		_settingsViewModel.StartInitialization();
	}

	private void OnInitializationCompleted(object? sender, InitializationCompletedEventArgs e)
	{
		// 如果检测失败，自动弹出设置对话框
		if (!e.IsBlenderDetected || !e.IsFFmpegDetected)
		{
			ShowSettingsDialog();
		}
		else
		{
			// 检测成功，直接应用设置
			ApplySettings(_settingsViewModel!.BlenderPath, _settingsViewModel.FfmpegPath);
		}
	}

	[RelayCommand]
	private void OpenSettings()
	{
		ShowSettingsDialog();
	}

	private void ShowSettingsDialog()
	{
		// 确保设置ViewModel存在
		if (_settingsViewModel == null)
		{
			InitializeSettings();
		}

		DialogManager.CreateDialog()
			.WithTitle("设置")
			.WithContent(_settingsViewModel)
			.WithActionButton("保存", _ => 
			{
				_settingsViewModel!.SaveSettingsCommand.Execute(null);
			}, true)
			.WithActionButton("取消", _ => { }, true)
			.Dismiss().ByClickingBackground()
			.TryShow();
	}

	private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
	{
		ApplySettings(e.BlenderPath, e.FfmpegPath);
	}

	private void ApplySettings(string blenderPath, string ffmpegPath)
	{
		// 将设置应用到主渲染视图模型
		if (Content is MainRenderViewModel mainRender)
		{
			mainRender.BlenderPath = blenderPath;
			mainRender.FfmpegPath = ffmpegPath;
		}
	}
}