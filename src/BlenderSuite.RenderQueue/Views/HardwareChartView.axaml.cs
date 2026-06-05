using System.Threading;
using Avalonia;
using Avalonia.Controls;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Views;

public partial class HardwareChartView : UserControl
{
    private static HardwareChartViewModel? _sharedViewModel;
    private static readonly Lock ViewModelLock = new();

    public HardwareChartView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        lock (ViewModelLock)
        {
            _sharedViewModel ??= new HardwareChartViewModel();
            DataContext = _sharedViewModel;
        }
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
