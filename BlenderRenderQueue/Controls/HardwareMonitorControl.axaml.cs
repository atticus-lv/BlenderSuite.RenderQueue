using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using BlenderRenderQueue.Services;

namespace BlenderRenderQueue.Controls;

public partial class HardwareMonitorControl : UserControl
{
    private readonly HardwareMonitorService _monitorService;
    private bool _isRunning = true;

    // CPU 使用率
    public static readonly DirectProperty<HardwareMonitorControl, float> CpuLoadProperty =
        AvaloniaProperty.RegisterDirect<HardwareMonitorControl, float>(
            nameof(CpuLoad),
            o => o.CpuLoad,
            (o, v) => o.CpuLoad = v);

    private float _cpuLoad;

    public float CpuLoad
    {
        get => _cpuLoad;
        private set => SetAndRaise(CpuLoadProperty, ref _cpuLoad, value);
    }

    // CPU 温度
    public static readonly DirectProperty<HardwareMonitorControl, float> CpuTemperatureProperty =
        AvaloniaProperty.RegisterDirect<HardwareMonitorControl, float>(
            nameof(CpuTemperature),
            o => o.CpuTemperature,
            (o, v) => o.CpuTemperature = v);

    private float _cpuTemperature;

    public float CpuTemperature
    {
        get => _cpuTemperature;
        private set => SetAndRaise(CpuTemperatureProperty, ref _cpuTemperature, value);
    }

    // 内存使用
    public static readonly DirectProperty<HardwareMonitorControl, float> MemoryUsedProperty =
        AvaloniaProperty.RegisterDirect<HardwareMonitorControl, float>(
            nameof(MemoryUsed),
            o => o.MemoryUsed,
            (o, v) => o.MemoryUsed = v);

    private float _memoryUsed;

    public float MemoryUsed
    {
        get => _memoryUsed;
        private set => SetAndRaise(MemoryUsedProperty, ref _memoryUsed, value);
    }

    // GPU 使用率
    public static readonly DirectProperty<HardwareMonitorControl, float> GpuLoadProperty =
        AvaloniaProperty.RegisterDirect<HardwareMonitorControl, float>(
            nameof(GpuLoad),
            o => o.GpuLoad,
            (o, v) => o.GpuLoad = v);

    private float _gpuLoad;

    public float GpuLoad
    {
        get => _gpuLoad;
        private set => SetAndRaise(GpuLoadProperty, ref _gpuLoad, value);
    }

    // GPU 温度
    public static readonly DirectProperty<HardwareMonitorControl, float> GpuTemperatureProperty =
        AvaloniaProperty.RegisterDirect<HardwareMonitorControl, float>(
            nameof(GpuTemperature),
            o => o.GpuTemperature,
            (o, v) => o.GpuTemperature = v);

    private float _gpuTemperature;

    public float GpuTemperature
    {
        get => _gpuTemperature;
        private set => SetAndRaise(GpuTemperatureProperty, ref _gpuTemperature, value);
    }

    // 添加内存使用百分比属性
    public static readonly DirectProperty<HardwareMonitorControl, float> MemoryUsedPercentageProperty =
        AvaloniaProperty.RegisterDirect<HardwareMonitorControl, float>(
            nameof(MemoryUsedPercentage),
            o => o.MemoryUsedPercentage,
            (o, v) => o.MemoryUsedPercentage = v);

    private float _memoryUsedPercentage;

    public float MemoryUsedPercentage
    {
        get => _memoryUsedPercentage;
        private set => SetAndRaise(MemoryUsedPercentageProperty, ref _memoryUsedPercentage, value);
    }

    public HardwareMonitorControl()
    {
        InitializeComponent();
        DataContext = this;
        _monitorService = new HardwareMonitorService();

        // 启动更新任务
        _ = StartUpdateLoop();
    }

    private async Task StartUpdateLoop()
    {
        while (_isRunning)
        {
            UpdateHardwareInfo();
            await Task.Delay(1000); // 每秒更新一次
        }
    }

    private void UpdateHardwareInfo()
    {
        var info = _monitorService.GetHardwareInfo();
        CpuLoad = info.CpuLoad;
        CpuTemperature = info.CpuTemperature;
        MemoryUsed = info.MemoryUsed;
        MemoryUsedPercentage = info.MemoryUsedPercentage;
        GpuLoad = info.GpuLoad;
        GpuTemperature = info.GpuTemperature;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isRunning = false;
        _monitorService.Dispose();
    }
}