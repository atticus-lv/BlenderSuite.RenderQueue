using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Views;

public partial class RenderTaskView : UserControl
{
    public RenderTaskView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnRenderedImageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RenderTaskViewModel viewModel)
        {
            viewModel.OpenImagePreviewCommand.Execute(null);
        }
    }
}