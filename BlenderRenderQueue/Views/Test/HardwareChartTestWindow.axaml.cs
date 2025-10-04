using Avalonia.Controls;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Views;

namespace BlenderRenderQueue.Views.Test;

public partial class HardwareChartTestWindow : Window
{
    public HardwareChartTestWindow()
    {
        InitializeComponent();
        DataContext = new HardwareChartView();
    }
}
