using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace BlenderRenderQueue.Services.Business.Monitoring;

public class HardwareMonitorService : IDisposable
{
    private static readonly Regex CpuUsageRegex = new(@"CPU usage:\s*(?<user>[\d.]+)% user,\s*(?<sys>[\d.]+)% sys,\s*(?<idle>[\d.]+)% idle", RegexOptions.Compiled);
    private static readonly Regex PhysMemRegex = new(@"PhysMem:\s*(?<used>[\d.]+[KMGTP]?) used.*,\s*(?<unused>[\d.]+[KMGTP]?) unused", RegexOptions.Compiled);
    private static readonly Regex GpuUtilizationRegex = new(@"""Device Utilization %""\s*=\s*(?<value>\d+)", RegexOptions.Compiled);

    private readonly Computer? _computer;

    public bool IsSupported { get; }

    public HardwareMonitorService()
    {
        if (OperatingSystem.IsWindows())
        {
            try
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
                IsSupported = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HardwareMonitorService] Failed to initialize Windows hardware monitor: {ex.Message}");
                _computer = null;
            }

            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            IsSupported = true;
        }
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
        if (!IsSupported)
        {
            return new HardwareInfo();
        }

        if (OperatingSystem.IsWindows())
        {
            return GetWindowsHardwareInfo();
        }

        if (OperatingSystem.IsMacOS())
        {
            return GetMacHardwareInfo();
        }

        return new HardwareInfo();
    }

    private HardwareInfo GetWindowsHardwareInfo()
    {
        var info = new HardwareInfo();
        if (_computer == null)
        {
            return info;
        }

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
                        if (sensor.SensorType == SensorType.Data && sensor.Name == "Memory")
                        {
                            memoryTotal = sensor.Value ?? 0;
                        }
                        else if (sensor.SensorType == SensorType.Load && sensor.Name == "Memory")
                        {
                            memoryUsedPercentage = sensor.Value ?? 0;
                        }
                    }

                    info.MemoryTotal = memoryTotal;
                    info.MemoryUsedPercentage = memoryUsedPercentage;
                    info.MemoryUsed = (memoryUsedPercentage / 100f) * memoryTotal;
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

    private HardwareInfo GetMacHardwareInfo()
    {
        var info = new HardwareInfo();

        try
        {
            var topOutput = RunCommand("/usr/bin/top", "-l 1 -stats cpu,mem");
            if (!string.IsNullOrWhiteSpace(topOutput))
            {
                PopulateMacCpuAndMemory(topOutput, info);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HardwareMonitorService] Failed to read macOS CPU/memory info: {ex.Message}");
        }

        try
        {
            var ioregOutput = RunCommand("/usr/sbin/ioreg", "-r -d 1 -c IOAccelerator");
            if (!string.IsNullOrWhiteSpace(ioregOutput))
            {
                PopulateMacGpu(ioregOutput, info);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HardwareMonitorService] Failed to read macOS GPU info: {ex.Message}");
        }

        return info;
    }

    private static void PopulateMacCpuAndMemory(string topOutput, HardwareInfo info)
    {
        var cpuMatch = CpuUsageRegex.Match(topOutput);
        if (cpuMatch.Success)
        {
            var user = ParseFloat(cpuMatch.Groups["user"].Value);
            var sys = ParseFloat(cpuMatch.Groups["sys"].Value);
            var idle = ParseFloat(cpuMatch.Groups["idle"].Value);
            var cpuLoad = user + sys;
            if (cpuLoad <= 0 && idle > 0)
            {
                cpuLoad = 100f - idle;
            }

            info.CpuLoad = Math.Clamp(cpuLoad, 0f, 100f);
        }

        var memoryMatch = PhysMemRegex.Match(topOutput);
        if (!memoryMatch.Success)
        {
            return;
        }

        var usedBytes = ParseMemoryValue(memoryMatch.Groups["used"].Value);
        var unusedBytes = ParseMemoryValue(memoryMatch.Groups["unused"].Value);
        var totalBytes = usedBytes + unusedBytes;
        if (totalBytes <= 0)
        {
            return;
        }

        info.MemoryUsed = (float)(usedBytes / 1024d / 1024d / 1024d);
        info.MemoryTotal = (float)(totalBytes / 1024d / 1024d / 1024d);
        info.MemoryUsedPercentage = Math.Clamp((float)(usedBytes * 100d / totalBytes), 0f, 100f);
    }

    private static void PopulateMacGpu(string ioregOutput, HardwareInfo info)
    {
        var matches = GpuUtilizationRegex.Matches(ioregOutput);
        var maxValue = 0f;
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var parsed = ParseFloat(match.Groups["value"].Value);
            if (parsed > maxValue)
            {
                maxValue = parsed;
            }
        }

        info.GpuLoad = Math.Clamp(maxValue, 0f, 100f);
    }

    private static string RunCommand(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(3000))
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // ignored
            }

            throw new TimeoutException($"Command timed out: {fileName} {arguments}");
        }

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
        {
            throw new InvalidOperationException(stderr.Trim());
        }

        return stdout;
    }

    private static float ParseFloat(string value)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0f;
    }

    private static double ParseMemoryValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var trimmed = value.Trim();
        var unit = trimmed[^1];
        double multiplier = 1;
        if (!char.IsLetter(unit))
        {
            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawValue)
                ? rawValue
                : 0;
        }

        trimmed = trimmed[..^1];
        multiplier = char.ToUpperInvariant(unit) switch
        {
            'K' => 1024d,
            'M' => 1024d * 1024d,
            'G' => 1024d * 1024d * 1024d,
            'T' => 1024d * 1024d * 1024d * 1024d,
            'P' => 1024d * 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue)
            ? numericValue * multiplier
            : 0;
    }

    public void Dispose()
    {
        _computer?.Close();
    }
}
