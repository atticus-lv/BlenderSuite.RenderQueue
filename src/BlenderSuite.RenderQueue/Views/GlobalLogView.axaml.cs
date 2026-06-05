using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BlenderSuite.RenderQueue.Views;

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
