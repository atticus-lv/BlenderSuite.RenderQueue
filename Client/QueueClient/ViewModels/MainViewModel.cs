using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlenderSuite.RenderQueue.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ConnectionViewModel _connectionViewModel;

    [ObservableProperty]
    private QueueInfoViewModel _queueInfoViewModel;

    [ObservableProperty]
    private ProgressViewModel _progressViewModel;

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public MainViewModel()
    {
        _connectionViewModel = new ConnectionViewModel();
        _queueInfoViewModel = new QueueInfoViewModel(_connectionViewModel);
        _progressViewModel = new ProgressViewModel(_connectionViewModel);
    }

    [RelayCommand]
    private void OnTabChanged(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
        
        // 当切换到队列信息或进度查看Tab时，自动刷新数据
        if (tabIndex == 1) // 队列信息Tab
        {
            _ = QueueInfoViewModel.RefreshCommand.ExecuteAsync(null);
        }
        else if (tabIndex == 2) // 进度查看Tab
        {
            _ = ProgressViewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ConnectionViewModel?.Dispose();
            QueueInfoViewModel?.Dispose();
            ProgressViewModel?.Dispose();
        }
        base.Dispose(disposing);
    }
}
