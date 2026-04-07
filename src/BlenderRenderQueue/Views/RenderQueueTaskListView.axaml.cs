using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Behaviors;
using Avalonia;
using Avalonia.Xaml.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace BlenderRenderQueue.Views;

public partial class RenderQueueTaskListView : UserControl
{
    public RenderQueueTaskListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // 确保拖拽行为正确附加
        EnsureDragBehaviorsAttached();
    }
    
    /// <summary>
    /// 确保拖拽行为正确附加到 ListBoxItem 控件上
    /// 这个方法在页面重新附加到视觉树时调用，解决页面切换后拖拽失效的问题
    /// </summary>
    private void EnsureDragBehaviorsAttached()
    {
        // 延迟执行，确保所有控件都已完全加载
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // 查找 ListBox 控件
                var listBox = this.FindControl<ListBox>("TaskListBox");
                if (listBox == null)
                {
                    // 如果没有找到命名的 ListBox，尝试通过遍历查找
                    listBox = FindListBoxRecursively(this);
                }
                
                if (listBox != null)
                {
                    // 遍历所有已实现的容器
                    foreach (var container in listBox.GetRealizedContainers())
                    {
                        if (container is ListBoxItem listBoxItem)
                        {
                            // 检查是否已经有 HandleDragBehavior
                            var existingBehavior = Interaction.GetBehaviors(listBoxItem)
                                .OfType<HandleDragBehavior>()
                                .FirstOrDefault();
                            
                            if (existingBehavior == null)
                            {
                                // 如果没有，添加一个新的 HandleDragBehavior
                                var dragBehavior = new HandleDragBehavior
                                {
                                    Orientation = Avalonia.Layout.Orientation.Vertical,
                                    HorizontalDragThreshold = 3,
                                    VerticalDragThreshold = 3,
                                    DragHandleTag = "DragHandle"
                                };
                                
                                Interaction.GetBehaviors(listBoxItem).Add(dragBehavior);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不影响应用程序运行
                System.Diagnostics.Debug.WriteLine($"确保拖拽行为附加时出错: {ex.Message}");
            }
        }, DispatcherPriority.Loaded);
    }
    
    /// <summary>
    /// 递归查找 ListBox 控件
    /// </summary>
    private ListBox? FindListBoxRecursively(Control parent)
    {
        if (parent is ListBox listBox)
        {
            return listBox;
        }

        return parent.GetVisualChildren().OfType<Control>().Select(child => FindListBoxRecursively(child)).OfType<ListBox>().FirstOrDefault();
    }
}
