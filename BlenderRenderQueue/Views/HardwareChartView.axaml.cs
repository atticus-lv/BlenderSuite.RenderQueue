using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Views;

public partial class HardwareChartView : UserControl
{
    public HardwareChartView()
    {
        InitializeComponent();
        DataContext = new HardwareChartViewModel();
        
        // 在控件加载完成后设置背景颜色
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 获取动态资源颜色并设置到ViewModel
        if (DataContext is HardwareChartViewModel viewModel)
        {
            if (TryGetResource("ControlSukiGlassCardBackground", null, out var resource) && resource is IBrush backgroundBrush)
            {
                if (backgroundBrush is SolidColorBrush solidBrush)
                {
                    viewModel.ChartBackgroundColor = solidBrush.Color.ToString();
                }
            }
        }
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
