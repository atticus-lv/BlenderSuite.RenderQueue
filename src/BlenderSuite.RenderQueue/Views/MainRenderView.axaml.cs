using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BlenderSuite.RenderQueue.ViewModels;
using SukiUI.Controls;

namespace BlenderSuite.RenderQueue.Views;

public partial class MainRenderView : UserControl
{
    private SukiSideMenu? _sideMenu;
    private SukiSideMenuItem? _queueMenuItem;
    private SukiSideMenuItem? _logsMenuItem;
    private SukiSideMenuItem? _settingsMenuItem;
    private SukiSideMenuItem? _infoMenuItem;
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
        _infoMenuItem = this.FindControl<SukiSideMenuItem>("InfoMenuItem");
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
        if (e.PropertyName == nameof(MainRenderViewModel.SelectedNavigationIndex) ||
            e.PropertyName == nameof(MainRenderViewModel.IsInfoPageVisible))
        {
            ApplySelectedNavigation();
        }
    }

    private void OnNavigationMenuItemTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel == null || sender is not Control control)
        {
            return;
        }

        if (!int.TryParse(control.Tag?.ToString(), out var navigationIndex))
        {
            return;
        }

        _viewModel.NavigateToNavigationIndex(navigationIndex);
    }

    private void ApplySelectedNavigation()
    {
        if (_sideMenu == null || _viewModel == null)
        {
            return;
        }

        if (_viewModel.IsInfoPageVisible)
        {
            _sideMenu.SelectedItem = _infoMenuItem;
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
