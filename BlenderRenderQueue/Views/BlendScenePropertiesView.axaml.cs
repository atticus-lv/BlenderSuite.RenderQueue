using Avalonia.Controls;
using Avalonia.Input;

namespace BlenderRenderQueue.Views;

public partial class BlendScenePropertiesView : UserControl
{
    public BlendScenePropertiesView()
    {
        InitializeComponent();
        
        // 订阅滚轮事件，将垂直滚动转换为水平滚动
        SceneScrollViewer.PointerWheelChanged += OnPointerWheelChanged;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            // 将垂直滚轮事件转换为水平滚动
            var delta = e.Delta.Y;
            var currentOffset = scrollViewer.Offset.X;
            var newOffset = currentOffset - (delta * 50); // 50是滚动速度，可以调整
            
            // 确保不超出滚动范围
            newOffset = System.Math.Max(0, System.Math.Min(newOffset, scrollViewer.Extent.Width - scrollViewer.Viewport.Width));
            
            scrollViewer.Offset = new Avalonia.Vector(newOffset, scrollViewer.Offset.Y);
            
            // 标记事件已处理，防止默认的垂直滚动行为
            e.Handled = true;
        }
    }
}
