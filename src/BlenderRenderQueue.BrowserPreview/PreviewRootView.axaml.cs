using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SukiUI.Controls;

namespace BlenderRenderQueue.BrowserPreview;

public partial class PreviewRootView : UserControl
{
    private SukiSideMenu? _sideMenu;
    private SukiSideMenuItem? _queueMenuItem;

    public PreviewRootView()
    {
        InitializeComponent();
        DataContext = new PreviewRootViewModel();
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
}
