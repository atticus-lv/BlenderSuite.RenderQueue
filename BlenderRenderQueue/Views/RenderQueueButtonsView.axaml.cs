using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Views;

public partial class RenderQueueButtonsView : UserControl
{
    public RenderQueueButtonsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
