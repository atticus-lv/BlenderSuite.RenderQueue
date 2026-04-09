using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BlenderRenderQueue.ViewModels;
using Avalonia;
using Avalonia.VisualTree;
using BlenderRenderQueue.Helpers;

namespace BlenderRenderQueue.Views;

public partial class RenderQueueView : UserControl
{
    private SplitView? _rightPanelBorder;
    private TopLevel? _topLevel;
    private RenderQueueViewModel? _viewModel;

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

        AttachViewModel(DataContext as RenderQueueViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachViewModel();

        if (_topLevel != null)
        {
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel = null;
        }
        
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachViewModel(DataContext as RenderQueueViewModel);
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

    private void AttachViewModel(RenderQueueViewModel? next)
    {
        if (ReferenceEquals(_viewModel, next))
        {
            return;
        }

        DetachViewModel();
        _viewModel = next;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(RenderQueueViewModel.SelectedTask), StringComparison.Ordinal))
        {
            return;
        }

        var task = _viewModel?.SelectedTask;
        if (task == null)
        {
            return;
        }

        SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderQueueView.SelectedTaskObserved");

        Dispatcher.UIThread.Post(() =>
        {
            SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderQueueView.PostBackground");
        }, DispatcherPriority.Background);

        Dispatcher.UIThread.Post(() =>
        {
            SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderQueueView.PostRender");
        }, DispatcherPriority.Render);

        Dispatcher.UIThread.Post(() =>
        {
            SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderQueueView.PostLoaded");
        }, DispatcherPriority.Loaded);
    }
}
