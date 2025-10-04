using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Behaviors;
using Avalonia;
using Avalonia.Xaml.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace BlenderRenderQueue.Views;

public partial class RenderQueueView : UserControl
{
    private SplitView? _rightPanelBorder;
    private TopLevel? _topLevel;

    public RenderQueueView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        
        // 获取右侧面板的Border控件
        _rightPanelBorder = this.FindControl<SplitView>("RightPanelBorder");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // 获取TopLevel（窗口）
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
        {
            _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
            // 初始更新
            UpdateRightPanelWidth();
        }
        
        // 确保拖拽行为正确附加
        EnsureDragBehaviorsAttached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel != null)
        {
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel = null;
        }
        
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 监听窗口的ClientSize变化
        if (e.Property == TopLevel.ClientSizeProperty)
        {
            UpdateRightPanelWidth();
        }
    }

    private void UpdateRightPanelWidth()
    {
        if (_rightPanelBorder != null && _topLevel != null)
        {
            var scale = 0.5;
            var windowWidth = _topLevel.ClientSize.Width;
            var calculatedWidth = windowWidth * scale;
            
            _rightPanelBorder.OpenPaneLength = calculatedWidth;
        }
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
                var listBox = this.FindControl<ListBox>("ListBox");
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
        
        foreach (var child in parent.GetVisualChildren().OfType<Control>())
        {
            var result = FindListBoxRecursively(child);
            if (result != null)
            {
                return result;
            }
        }
        
        return null;
    }
}