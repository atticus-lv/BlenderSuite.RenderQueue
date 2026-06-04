using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BlenderRenderQueue.ViewModels;
using SukiUI;
using SukiUI.Controls;

namespace BlenderRenderQueue.BrowserPreview;

public partial class PreviewRootView : UserControl
{
    private SukiSideMenu? _sideMenu;
    private SukiSideMenuItem? _queueMenuItem;

    public PreviewRootView()
    {
        InitializeComponent();
        var viewModel = new PreviewRootViewModel();
        DataContext = viewModel;
        viewModel.Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.BaseTheme))
            {
                ApplyPreviewTheme(viewModel.Settings.BaseTheme.Value);
            }
        };
        ApplyPreviewTheme(viewModel.Settings.BaseTheme.Value);

        if (_sideMenu != null)
        {
            _sideMenu.SelectedItem = _queueMenuItem;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _sideMenu = this.FindControl<SukiSideMenu>("PreviewSideMenu");
        _queueMenuItem = this.FindControl<SukiSideMenuItem>("QueueMenuItem");
    }

    private void ApplyPreviewTheme(string theme)
    {
        var isLight = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        var variant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;

        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant;
            SukiTheme.GetInstance(Application.Current).ChangeBaseTheme(variant);
        }
    }
}
