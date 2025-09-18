using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BlenderRenderQueue.Services.BlenderService;

public class BlenderExeService : BasePythonProcessService
{
    private readonly string _blenderPath;

    public BlenderExeService(string blenderPath)
    {
        _blenderPath = blenderPath;
        InitializeProcess();
    }

    protected override bool ValidateEnvironment()
    {
        // 检查Blender可执行文件是否存在
        if (!File.Exists(_blenderPath))
        {
            RaiseErrorReceived($"Blender可执行文件不存在: {_blenderPath}");
            return false;
        }

        // 验证是否为Blender可执行文件
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(_blenderPath);
            if (!versionInfo.FileName.Contains("blender", StringComparison.OrdinalIgnoreCase))
            {
                RaiseErrorReceived($"指定的文件不是Blender可执行文件: {_blenderPath}");
                return false;
            }
        }
        catch (Exception ex)
        {
            RaiseErrorReceived($"验证Blender可执行文件失败: {ex.Message}");
            return false;
        }

        return true;
    }

    protected override void CreateProcess()
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _blenderPath,
                Arguments = "--background --log-level info --python-console",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8
            }
        };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            RaiseOutputReceived(e.Data);
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            RaiseErrorReceived($"Error: {e.Data}");
        };

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"启动Blender进程失败: {ex.Message}", ex);
        }
    }
}