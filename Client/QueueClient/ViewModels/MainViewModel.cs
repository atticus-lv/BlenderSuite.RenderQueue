using CommunityToolkit.Mvvm.ComponentModel;

namespace BlenderSuite.RenderQueue.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
}
