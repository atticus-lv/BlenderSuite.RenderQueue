namespace BlenderRenderQueue.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
	public ViewModelBase Content { get; }

	public MainWindowViewModel()
	{
		Content = new BlenderRenderQueue.ViewModels.Test.TestRenderViewModel();
	}
}