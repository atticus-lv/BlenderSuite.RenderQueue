using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;

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

    private void OnRenderedImageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RenderQueueViewModel viewModel && viewModel.SelectedTask != null)
        {
            viewModel.SelectedTask.OpenImagePreviewCommand.Execute(null);
        }
    }
}
