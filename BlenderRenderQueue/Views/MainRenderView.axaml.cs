using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BlenderRenderQueue.Views;

public partial class MainRenderView : UserControl
{
    public MainRenderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
