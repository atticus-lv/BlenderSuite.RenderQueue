using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BlenderSuite.RenderQueue.ViewModels;
using BlenderSuite.RenderQueue.Views;
using BlenderSuite.RenderQueue.Views.Test;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderSuite.RenderQueue;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = AppServices.Instance.GetRequiredService<MainWindowViewModel>(),
            };

            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // 清理共享的硬件监控ViewModel
        HardwareChartView.CleanupSharedViewModel();
    }
}
