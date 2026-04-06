using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;
using SukiUI.Controls;

namespace BlenderRenderQueue.Views;

public partial class MainRenderView : UserControl
{
    private SukiSideMenu? _sideMenu;
    private SukiSideMenuItem? _queueMenuItem;
    private SukiSideMenuItem? _logsMenuItem;
    private SukiSideMenuItem? _settingsMenuItem;
    private MainRenderViewModel? _viewModel;

    public MainRenderView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => HookViewModel();
        DataContextChanged += (_, _) => HookViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _sideMenu = this.FindControl<SukiSideMenu>("SideMenu");
        _queueMenuItem = this.FindControl<SukiSideMenuItem>("QueueMenuItem");
        _logsMenuItem = this.FindControl<SukiSideMenuItem>("LogsMenuItem");
        _settingsMenuItem = this.FindControl<SukiSideMenuItem>("SettingsMenuItem");
    }

    private void HookViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainRenderViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplySelectedNavigation();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainRenderViewModel.SelectedNavigationIndex))
        {
            ApplySelectedNavigation();
        }
    }

    private void ApplySelectedNavigation()
    {
        if (_sideMenu == null || _viewModel == null)
        {
            return;
        }

        _sideMenu.SelectedItem = _viewModel.SelectedNavigationIndex switch
        {
            1 => _logsMenuItem,
            2 => _settingsMenuItem,
            _ => _queueMenuItem
        };
    }
}
