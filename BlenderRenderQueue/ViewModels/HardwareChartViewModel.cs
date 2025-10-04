using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BlenderRenderQueue.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore.Defaults;

namespace BlenderRenderQueue.ViewModels;

public partial class HardwareChartViewModel : ViewModelBase
{
    private readonly HardwareMonitorService _monitorService;
    private bool _isReading = true;
    private readonly object _sync = new();
    
    // 数据点数量限制 - 1分钟，每秒1个数据点
    private const int MaxDataPoints = 60;
    
    // 图表数据集合
    public ObservableCollection<ObservableValue> CpuLoadValues { get; set; } = [];
    public ObservableCollection<ObservableValue> GpuLoadValues { get; set; } = [];
    
    // 同步上下文
    public object Sync => _sync;
    
    public HardwareChartViewModel()
    {
        _monitorService = new HardwareMonitorService();
        _ = ReadData();
    }
    
    private async Task ReadData()
    {
        while (_isReading)
        {
            try
            {
                await Task.Delay(1000); // 每秒更新一次
                
                var info = _monitorService.GetHardwareInfo();
                
                // 使用锁来确保线程安全
                lock (_sync)
                {
                    // 更新CPU使用率数据
                    CpuLoadValues.Add(new ObservableValue(info.CpuLoad));
                    if (CpuLoadValues.Count > MaxDataPoints) 
                    {
                        CpuLoadValues.RemoveAt(0); // 移除最旧的数据点
                    }
                    
                    // 更新GPU使用率数据
                    GpuLoadValues.Add(new ObservableValue(info.GpuLoad));
                    if (GpuLoadValues.Count > MaxDataPoints) 
                    {
                        GpuLoadValues.RemoveAt(0); // 移除最旧的数据点
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不中断循环
                System.Diagnostics.Debug.WriteLine($"硬件监控更新错误: {ex.Message}");
            }
        }
    }
    
    public void Dispose()
    {
        _isReading = false;
        _monitorService?.Dispose();
    }
}
