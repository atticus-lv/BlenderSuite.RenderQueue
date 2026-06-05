using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Views;

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
