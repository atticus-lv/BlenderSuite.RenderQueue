using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BlenderRenderQueue.ViewModels;
using Avalonia;

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
}