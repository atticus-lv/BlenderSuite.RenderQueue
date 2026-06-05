using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BlenderSuite.RenderQueue.Helpers;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Views;

public partial class RenderTaskView : UserControl
{
    public RenderTaskView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not RenderTaskViewModel task)
        {
            return;
        }

        var details =
            $"timeline={task.TimelineEntries.Count} debug={task.DebugEntries.Count} outputChars={task.OutputLog.Length} framePath={task.FramePathDirectory ?? "<null>"} hasRenderedImage={task.HasRenderedImage}";
        SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderTaskView.DataContextChanged", details);

        Dispatcher.UIThread.Post(() =>
        {
            SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderTaskView.PostLoaded");
        }, DispatcherPriority.Loaded);

        Dispatcher.UIThread.Post(() =>
        {
            SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderTaskView.PostRender");
        }, DispatcherPriority.Render);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is not RenderTaskViewModel task)
        {
            return;
        }

        SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "RenderTaskView.AttachedToVisualTree");
    }
}
