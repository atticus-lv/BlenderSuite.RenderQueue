using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BlenderSuite.RenderQueue.Views;

public partial class RenderQueueTaskListView : UserControl
{
    public RenderQueueTaskListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
