using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QueueClient.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ConnectionViewModel _connectionViewModel;

    [ObservableProperty]
    private QueueInfoViewModel _queueInfoViewModel;

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public MainViewModel()
    {
        _connectionViewModel = new QueueClient.ViewModels.ConnectionViewModel();
        _queueInfoViewModel = new QueueInfoViewModel(_connectionViewModel);
    }

    [RelayCommand]
    private void OnTabChanged(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
        
        // 当切换到队列信息Tab时，自动刷新数据
        if (tabIndex == 1) // 队列信息Tab
        {
            _ = QueueInfoViewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ConnectionViewModel?.Dispose();
            QueueInfoViewModel?.Dispose();
        }
        base.Dispose(disposing);
    }
}
