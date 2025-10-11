using System.Threading.Tasks;
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
    private int _selectedTabIndex;

    public MainViewModel()
    {
        _connectionViewModel = new QueueClient.ViewModels.ConnectionViewModel();
        _queueInfoViewModel = new QueueInfoViewModel(_connectionViewModel);
        
        // 启动时自动尝试连接
        _ = InitializeAsync();
    }

    /// <summary>
    /// 初始化时自动尝试连接
    /// </summary>
    private async Task InitializeAsync()
    {
        // 等待一小段时间确保UI完全加载
        await Task.Delay(500);
        
        // 自动尝试连接
        await ConnectionViewModel.ConnectCommand.ExecuteAsync(null);
        
        // 如果连接成功，自动跳转到队列信息界面
        if (ConnectionViewModel.IsConnected)
        {
            SelectedTabIndex = 1; // 跳转到队列信息Tab
        }
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
