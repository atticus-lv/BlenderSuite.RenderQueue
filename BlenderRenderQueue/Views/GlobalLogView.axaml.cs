using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BlenderRenderQueue.Views;

public partial class GlobalLogView : UserControl
{
    public GlobalLogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
