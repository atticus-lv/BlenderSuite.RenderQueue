using System;
using Avalonia.Controls;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Controls;
using SukiUI.Controls;

namespace BlenderRenderQueue.Views;

public partial class ImagePreviewWindow : SukiWindow
{
    private ImagePreviewControl? _imagePreviewControl;
    
    public ImagePreviewWindow()
    {
        InitializeComponent();
    }
    
    public ImagePreviewWindow(ImagePreviewWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        
        // 绑定ImagePreviewControl的属性
        if (viewModel != null)
        {
            viewModel.PropertyChanged += (sender, e) =>
            {
                if (_imagePreviewControl != null)
                {
                    switch (e.PropertyName)
                    {
                        case nameof(ImagePreviewWindowViewModel.Image):
                            _imagePreviewControl.Image = viewModel.Image;
                            break;
                        case nameof(ImagePreviewWindowViewModel.IsLoading):
                            _imagePreviewControl.IsLoading = viewModel.IsLoading;
                            break;
                    }
                }
            };
        }
    }
    
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // 查找ImagePreviewControl
        _imagePreviewControl = this.FindControl<ImagePreviewControl>("ImagePreviewControl");
        
        // 如果找到了控件且DataContext是ImagePreviewWindowViewModel，则设置初始值
        if (_imagePreviewControl != null && DataContext is ImagePreviewWindowViewModel viewModel)
        {
            _imagePreviewControl.Image = viewModel.Image;
            _imagePreviewControl.IsLoading = viewModel.IsLoading;
        }
    }
    
    public void ShowWindow()
    {
        Show();
    }
}
