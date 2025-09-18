using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BlenderRenderQueue.Views;

public partial class RenderQueueView : UserControl
{
    public RenderQueueView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
