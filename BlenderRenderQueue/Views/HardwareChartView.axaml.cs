using Avalonia.Controls;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Views;

public partial class HardwareChartView : UserControl
{
    public HardwareChartView()
    {
        InitializeComponent();
        DataContext = new HardwareChartViewModel();
    }
    
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // 清理资源
        if (DataContext is HardwareChartViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}
