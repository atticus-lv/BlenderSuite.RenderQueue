using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Views;

namespace BlenderRenderQueue;

public class ViewLocator : IDataTemplate
{
    // AOT compiled code - 使用字典映射避免反射
    private static readonly Dictionary<Type, Func<Control>> ViewModelViewMap = new()
    {
        { typeof(MainWindowViewModel), () => new MainWindow() },
        { typeof(MainRenderViewModel), () => new MainRenderView() },
        { typeof(RenderQueueViewModel), () => new RenderQueueView() },
        { typeof(GlobalLogViewModel), () => new GlobalLogView() },
        { typeof(RenderTaskViewModel), () => new RenderTaskView() },
        { typeof(BlendScenePropertiesViewModel), () => new BlendScenePropertiesView() },
        { typeof(ImagePreviewWindowViewModel), () => new ImagePreviewWindow() },
        { typeof(SettingsViewModel), () => new SettingsView() },
        { typeof(HardwareChartViewModel), () => new HardwareChartView() },
    };

    public Control? Build(object? data)
    {
        if (data is null) return null;
        
        var viewModelType = data.GetType();
        if (!ViewModelViewMap.TryGetValue(viewModelType, out var viewFactory))
            return new TextBlock { Text = "Not Found: " + viewModelType.FullName };
        
        var control = viewFactory();
        control.DataContext = data;
        return control;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
