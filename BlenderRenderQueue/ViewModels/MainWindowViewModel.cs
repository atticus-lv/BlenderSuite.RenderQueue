namespace BlenderRenderQueue.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
	public ViewModelBase Content { get; }

	public MainWindowViewModel()
	{
		// 使用新的渲染队列视图模型替代测试视图模型
		Content = new MainRenderViewModel();
	}
}