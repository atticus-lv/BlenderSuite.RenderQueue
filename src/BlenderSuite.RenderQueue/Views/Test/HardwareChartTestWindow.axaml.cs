using Avalonia.Controls;
using BlenderSuite.RenderQueue.ViewModels;
using BlenderSuite.RenderQueue.Views;

namespace BlenderSuite.RenderQueue.Views.Test;

public partial class HardwareChartTestWindow : Window
{
    public HardwareChartTestWindow()
    {
        InitializeComponent();
        DataContext = new HardwareChartView();
    }
}
