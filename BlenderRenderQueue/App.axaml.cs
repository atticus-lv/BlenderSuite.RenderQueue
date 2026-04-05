using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Views;
using BlenderRenderQueue.Views.Test;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderRenderQueue;

public partial class App : Application
{
    private ILocalSubmissionHost? _localSubmissionHost;
    private bool _localSubmissionHostStopped;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
            BindingPlugins.DataValidators.RemoveAt(0);
            desktop.MainWindow = new MainWindow
            {
                DataContext = AppServices.Instance.GetRequiredService<MainWindowViewModel>(),
            };

            try
            {
                _localSubmissionHost = AppServices.Instance.GetRequiredService<ILocalSubmissionHost>();
                Dispatcher.UIThread.Post(() => _ = StartLocalSubmissionHostAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Failed to start local submission host: {ex.Message}");
            }

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        StopLocalSubmissionHost();

        // 清理共享的硬件监控ViewModel
        HardwareChartView.CleanupSharedViewModel();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        StopLocalSubmissionHost();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        StopLocalSubmissionHost();
    }

    private async Task StartLocalSubmissionHostAsync()
    {
        if (_localSubmissionHost == null)
        {
            return;
        }

        try
        {
            var mainRenderViewModel = AppServices.Instance.GetRequiredService<MainRenderViewModel>();
            await mainRenderViewModel.InitialLoadTask;
            await _localSubmissionHost.StartAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] Failed to start local submission host: {ex.Message}");
        }
    }

    private void StopLocalSubmissionHost()
    {
        if (_localSubmissionHostStopped)
        {
            return;
        }

        _localSubmissionHostStopped = true;

        try
        {
            _localSubmissionHost?.ShutdownAsync().GetAwaiter().GetResult();
            _localSubmissionHost?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] Failed to stop local submission host: {ex.Message}");
        }
    }
}
