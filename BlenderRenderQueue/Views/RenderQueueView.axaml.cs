using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Views;

public partial class RenderQueueView : UserControl
{
    public RenderQueueView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ToggleDeviceInfoPane(object? sender, RoutedEventArgs e)
    {
        // 通过名称查找 SplitView 控件
        var splitView = this.FindControl<SplitView>("DeviceInfoSplitView");
        if (splitView != null)
        {
            splitView.IsPaneOpen = !splitView.IsPaneOpen;
        }

    }
}