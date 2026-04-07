using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.Monitoring;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore.Defaults;

namespace BlenderRenderQueue.ViewModels;

public partial class HardwareChartViewModel : ViewModelBase
{
    private HardwareMonitorService? _monitorService;
    private bool _isReading = false;
    private bool _isInitialized = false;
    private readonly object _sync = new();

    // 数据点数量限制 - 1分钟，每秒1个数据点
    private const int MaxDataPoints = 60;

    // 图表数据集合
    public ObservableCollection<ObservableValue> CpuLoadValues { get; set; } = [];
    public ObservableCollection<ObservableValue> GpuLoadValues { get; set; } = [];

    // 同步上下文
    public object Sync => _sync;

    // 加载状态属性
    [ObservableProperty]
    private bool _isLoading = true;
    
    // 图表背景颜色属性
    [ObservableProperty]
    private string _chartBackgroundColor = "#00000000"; // 默认透明


    public HardwareChartViewModel()
    {
        // 延迟初始化，不立即启动监控
        _ = InitializeAsync();
    }

    /// <summary>
    /// 异步初始化硬件监控服务
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            // 延迟2秒，让主界面先完成加载
            await Task.Delay(2000);


            // 在后台线程初始化服务
            await Task.Run(() => { _monitorService = new HardwareMonitorService(); });


            // 启动数据读取
            _isReading = true;
            _isInitialized = true;
            IsLoading = false;

            // 开始数据读取循环
            _ = ReadData();
        }
        catch (Exception ex)
        {
            IsLoading = false;
            System.Diagnostics.Debug.WriteLine($"硬件监控初始化错误: {ex.Message}");
        }
    }

    private async Task ReadData()
    {
        // 等待初始化完成
        while (!_isInitialized && _isReading)
        {
            await Task.Delay(100);
        }

        while (_isReading && _monitorService != null)
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
        _isInitialized = false;
        _monitorService?.Dispose();
        _monitorService = null;
    }
}