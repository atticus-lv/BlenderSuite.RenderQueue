using Avalonia.Controls;
using Avalonia.Interactivity;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Views;

public partial class RenderTaskProgressPreviewView : UserControl
{
    public RenderTaskProgressPreviewView()
    {
        InitializeComponent();
    }

    private void OnRenderedImageDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RenderTaskViewModel viewModel)
        {
            viewModel.OpenImagePreviewCommand.Execute(null);
        }
    }
}