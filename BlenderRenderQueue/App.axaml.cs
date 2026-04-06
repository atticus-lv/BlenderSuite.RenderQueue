using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Views;
using BlenderRenderQueue.Views.Test;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderRenderQueue;

public partial class App : Application
{
    private ILocalSubmissionHost? _localSubmissionHost;
    private IRenderLogService? _renderLogService;
    private readonly object _localSubmissionHostStopLock = new();
    private Task? _localSubmissionHostStopTask;
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
                _renderLogService = AppServices.Instance.GetRequiredService<IRenderLogService>();
                _localSubmissionHost = AppServices.Instance.GetRequiredService<ILocalSubmissionHost>();
                Dispatcher.UIThread.Post(() => _ = StartLocalSubmissionHostAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Failed to start local submission host: {ex.Message}");
                _renderLogService?.Write(RenderLogLevel.Error, RenderLogScope.Submission, $"启动本地 submission host 失败: {ex.Message}", source: nameof(App));
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
            _renderLogService?.Write(RenderLogLevel.Error, RenderLogScope.Submission, $"本地 submission host 启动失败: {ex.Message}", source: nameof(App));
        }
    }

    private void StopLocalSubmissionHost()
    {
        _ = StopLocalSubmissionHostAsync();
    }

    private Task StopLocalSubmissionHostAsync()
    {
        lock (_localSubmissionHostStopLock)
        {
            if (_localSubmissionHostStopTask != null)
            {
                return _localSubmissionHostStopTask;
            }

            if (_localSubmissionHostStopped)
            {
                return Task.CompletedTask;
            }

            _localSubmissionHostStopped = true;
            var host = _localSubmissionHost;
            _localSubmissionHost = null;

            _localSubmissionHostStopTask = Task.Run(async () =>
            {
                if (host == null)
                {
                    return;
                }

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await host.ShutdownAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _renderLogService?.Write(
                        RenderLogLevel.Warning,
                        RenderLogScope.Submission,
                        "停止本地 submission host 超时，已切换为后台收尾。",
                        source: nameof(App));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] Failed to stop local submission host: {ex.Message}");
                    _renderLogService?.Write(
                        RenderLogLevel.Warning,
                        RenderLogScope.Submission,
                        $"停止本地 submission host 失败: {ex.Message}",
                        source: nameof(App));
                }
                finally
                {
                    try
                    {
                        host.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[App] Failed to dispose local submission host: {ex.Message}");
                    }
                }
            });

            return _localSubmissionHostStopTask;
        }
    }
}
