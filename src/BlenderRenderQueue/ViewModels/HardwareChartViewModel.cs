using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using BlenderRenderQueue.Services.Business.Monitoring;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BlenderRenderQueue.ViewModels;

public partial class HardwareChartViewModel : ViewModelBase
{
    private const int MaxDataPoints = 60;

    private HardwareMonitorService? _monitorService;
    private bool _isReading;
    private bool _isInitialized;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();

    [ObservableProperty]
    private IReadOnlyList<double> _cpuLoadValues = Array.Empty<double>();

    [ObservableProperty]
    private IReadOnlyList<double> _gpuLoadValues = Array.Empty<double>();

    [ObservableProperty]
    private bool _isLoading = true;

    public HardwareChartViewModel()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Task.Delay(2000);
            await Task.Run(() => _monitorService = new HardwareMonitorService());

            _isReading = true;
            _isInitialized = true;
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);

            _ = ReadDataAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            System.Diagnostics.Debug.WriteLine($"硬件监控初始化错误: {ex.Message}");
        }
    }

    private async Task ReadDataAsync()
    {
        while (!_isInitialized && _isReading)
        {
            await Task.Delay(100);
        }

        while (_isReading && _monitorService != null)
        {
            try
            {
                await Task.Delay(1000);
                var info = _monitorService.GetHardwareInfo();
                await Dispatcher.UIThread.InvokeAsync(() => AppendSample(info));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"硬件监控更新错误: {ex.Message}");
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
        _monitorService?.Dispose();
        _monitorService = null;
    }
}
