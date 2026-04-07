using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Views;

public partial class HardwareChartView : UserControl
{
    private static HardwareChartViewModel? _sharedViewModel;
    private static readonly Lock ViewModelLock = new();
    
    public HardwareChartView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // Reuse the shared ViewModel and create a new one if it doesn't exist
        lock (ViewModelLock)
        {
            _sharedViewModel ??= new HardwareChartViewModel();
            DataContext = _sharedViewModel;
        }
        
        // 延迟设置背景颜色，确保ViewModel已经创建
        Dispatcher.UIThread.Post(SetBackgroundColor, DispatcherPriority.Loaded);
    }
    
    private void SetBackgroundColor()
    {
        // 获取动态资源颜色并设置到ViewModel
        lock (ViewModelLock)
        {
            if (_sharedViewModel == null) return;
            if (!TryGetResource("ControlSukiGlassCardBackground", null, out var resource) ||
                resource is not IBrush backgroundBrush) return;
            if (backgroundBrush is not SolidColorBrush solidBrush) return;
            var newColor = solidBrush.Color.ToString();

            if (_sharedViewModel.ChartBackgroundColor != newColor)
            {
                _sharedViewModel.ChartBackgroundColor = newColor;
            }
        }
    }
    
    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {

    }
    
    
    /// <summary>
    /// A static method to clean up a shared ViewModel when the application is closed
    /// </summary>
    public static void CleanupSharedViewModel()
    {
        lock (ViewModelLock)
        {
            if (_sharedViewModel == null) return;
            _sharedViewModel.Dispose();
            _sharedViewModel = null;
        }
    }
}
