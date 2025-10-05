using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Views;

public partial class HardwareChartView : UserControl
{
    private static HardwareChartViewModel? _sharedViewModel;
    private static readonly object _viewModelLock = new();
    
    public HardwareChartView()
    {
        InitializeComponent();
        
        // 在控件加载完成后设置背景颜色
        Loaded += OnLoaded;
    }
    
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // 重用共享的ViewModel，如果不存在则创建新的
        lock (_viewModelLock)
        {
            if (_sharedViewModel == null)
            {
                _sharedViewModel = new HardwareChartViewModel();
            }
            
            DataContext = _sharedViewModel;
        }
        
        // 延迟设置背景颜色，确保ViewModel已经创建
        Dispatcher.UIThread.Post(() => SetBackgroundColor(), DispatcherPriority.Loaded);
    }
    
    private void SetBackgroundColor()
    {
        // 获取动态资源颜色并设置到ViewModel
        lock (_viewModelLock)
        {
            if (_sharedViewModel != null)
            {
                if (TryGetResource("ControlSukiGlassCardBackground", null, out var resource) && resource is IBrush backgroundBrush)
                {
                    if (backgroundBrush is SolidColorBrush solidBrush)
                    {
                        var newColor = solidBrush.Color.ToString();
                        // 只有当颜色发生变化时才更新，避免不必要的更新
                        if (_sharedViewModel.ChartBackgroundColor != newColor)
                        {
                            _sharedViewModel.ChartBackgroundColor = newColor;
                        }
                    }
                }
            }
        }
    }
    
    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 背景颜色设置已经移到SetBackgroundColor方法中
        // 这里可以添加其他需要在Loaded时执行的逻辑
    }
    
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // 不在这里清理共享的ViewModel，让它继续运行
        // 只有在应用程序关闭时才会清理
    }
    
    /// <summary>
    /// 静态方法，用于在应用程序关闭时清理共享的ViewModel
    /// </summary>
    public static void CleanupSharedViewModel()
    {
        lock (_viewModelLock)
        {
            if (_sharedViewModel != null)
            {
                _sharedViewModel.Dispose();
                _sharedViewModel = null;
            }
        }
    }
}
