using System;
using LibreHardwareMonitor.Hardware;

namespace BlenderRenderQueue.Services.Business.Monitoring;

public class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    
    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true
        };
        
        _computer.Open();
    }

    public class HardwareInfo
    {
        public float CpuTemperature { get; set; }
        public float CpuLoad { get; set; }
        public float MemoryUsed { get; set; }
        public float MemoryTotal { get; set; }
        public float MemoryUsedPercentage { get; set; }
        public float GpuTemperature { get; set; }
        public float GpuLoad { get; set; }
    }

    public HardwareInfo GetHardwareInfo()
    {
        var info = new HardwareInfo();
        
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            
            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("CPU Package"))
                        {
                            info.CpuTemperature = sensor.Value ?? 0;
                        }
                        else if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("CPU Total"))
                        {
                            info.CpuLoad = sensor.Value ?? 0;
                        }
                    }
                    break;
                
                case HardwareType.Memory:
                    float memoryTotal = 0;
                    float memoryUsedPercentage = 0;

                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Data)
                        {
                            if (sensor.Name == "Memory")
                            {
                                memoryTotal = sensor.Value ?? 0;
                            }
                        }
                        else if (sensor.SensorType == SensorType.Load)
                        {
                            if (sensor.Name == "Memory")
                            {
                                memoryUsedPercentage = sensor.Value ?? 0;
                            }
                        }
                    }

                    info.MemoryTotal = memoryTotal;
                    info.MemoryUsedPercentage = memoryUsedPercentage;
                    info.MemoryUsed = (memoryUsedPercentage / 100) * memoryTotal;
                    break;
                
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("GPU Core"))
                        {
                            info.GpuTemperature = sensor.Value ?? 0;
                        }
                        else if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("GPU Core"))
                        {
                            info.GpuLoad = sensor.Value ?? 0;
                        }
                    }
                    break;
            }
        }
        
        return info;
    }

    public void Dispose()
    {
        _computer.Close();
    }
}