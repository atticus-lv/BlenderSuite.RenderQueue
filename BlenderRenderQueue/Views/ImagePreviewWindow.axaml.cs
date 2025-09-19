using Avalonia.Controls;
using BlenderRenderQueue.ViewModels;
using SukiUI.Controls;

namespace BlenderRenderQueue.Views;

public partial class ImagePreviewWindow : SukiWindow
{
    public ImagePreviewWindow()
    {
        InitializeComponent();
    }
    
    public ImagePreviewWindow(ImagePreviewWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
    
    public void ShowWindow()
    {
        Show();
    }
}
