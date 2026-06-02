using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BlenderRenderQueue.Extensions;
using BlenderRenderQueue.Services.Business.Monitoring;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BlenderRenderQueue.ViewModels;

public partial class HardwareChartViewModel : ViewModelBase
{
    private const int MaxDataPoints = 60;

    private HardwareMonitorService? _monitorService;
    private bool _isReading;
    private bool _isInitialized;
    private readonly object _pendingInfoGate = new();
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();
    private CancellationTokenSource? _readLoopCts = new();
    private HardwareMonitorService.HardwareInfo? _pendingInfo;
    private bool _uiUpdateQueued;

    [ObservableProperty]
    private IReadOnlyList<double> _cpuLoadValues = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _gpuLoadValues = Array.Empty<double>();

    [ObservableProperty]
    private bool _isLoading = true;

    public HardwareChartViewModel()
    {
        InitializeAsync().FireAndForget(
            source: nameof(HardwareChartViewModel),
            message: "硬件监控初始化后台任务失败。");
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Task.Delay(2000);
            await Task.Run(() => _monitorService = new HardwareMonitorService());

            _isReading = true;
            _isInitialized = true;
            Dispatcher.UIThread.Post(() => IsLoading = false, DispatcherPriority.Background);

            Task.Run(() => ReadDataLoopAsync(_readLoopCts?.Token ?? CancellationToken.None)).FireAndForget(
                source: nameof(HardwareChartViewModel),
                message: "硬件监控读取循环后台任务失败。");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => IsLoading = false, DispatcherPriority.Background);
            System.Diagnostics.Debug.WriteLine($"硬件监控初始化错误: {ex.Message}");
        }
    }

    private async Task ReadDataLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            while (!cancellationToken.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(cancellationToken) &&
                   _isReading &&
                   _monitorService != null)
            {
                try
                {
                    var info = _monitorService.GetHardwareInfo();
                    QueueUiUpdate(info);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"硬件监控更新错误: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during shutdown.
        }
    }

    private void QueueUiUpdate(HardwareMonitorService.HardwareInfo info)
    {
        var shouldPost = false;

        lock (_pendingInfoGate)
        {
            _pendingInfo = info;

            if (!_uiUpdateQueued)
            {
                _uiUpdateQueued = true;
                shouldPost = true;
            }
        }

        if (shouldPost)
        {
            Dispatcher.UIThread.Post(ApplyPendingSample, DispatcherPriority.Background);
        }
    }

    private void ApplyPendingSample()
    {
        while (true)
        {
            HardwareMonitorService.HardwareInfo? info;
            lock (_pendingInfoGate)
            {
                info = _pendingInfo;
                _pendingInfo = null;

                if (info == null)
                {
                    _uiUpdateQueued = false;
                    return;
                }
            }

            if (_isReading && _isInitialized)
            {
                AppendSample(info);
            }
        }
    }

    private void AppendSample(HardwareMonitorService.HardwareInfo info)
    {
        AppendValue(_cpuHistory, info.CpuLoad);
        AppendValue(_gpuHistory, info.GpuLoad);

        CpuLoadValues = _cpuHistory.ToArray();
        GpuLoadValues = _gpuHistory.ToArray();
    }

    private static void AppendValue(Queue<double> history, float value)
    {
        history.Enqueue(Math.Clamp((double)value, 0d, 100d));
        while (history.Count > MaxDataPoints)
        {
            history.Dequeue();
        }
    }

    public void Dispose()
    {
        _isReading = false;
        _isInitialized = false;
        lock (_pendingInfoGate)
        {
            _pendingInfo = null;
            _uiUpdateQueued = false;
        }

        _readLoopCts?.Cancel();
        _readLoopCts?.Dispose();
        _readLoopCts = null;
        _monitorService?.Dispose();
        _monitorService = null;
    }
}
