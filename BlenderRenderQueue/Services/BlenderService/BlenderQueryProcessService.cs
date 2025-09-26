using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

/// <summary>
/// 专门用于查询操作的临时Blender进程服务
/// 每次查询完成后自动释放进程
/// </summary>
public class BlenderQueryProcessService : BasePythonProcessService
{
    private readonly string _blenderPath;
    
    public string BlenderPath => _blenderPath;

    public BlenderQueryProcessService(string blenderPath)
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
                Arguments = "--background --factory-startup --log-level info --python-console",
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
            
            // 过滤掉一些常见的Blender警告信息，这些不应该被当作错误
            var data = e.Data;
            // 检查是否是真正的Blender崩溃（显著特征：输出"Blender quit"）
            var isBlenderCrash = data.Contains("Blender quit", StringComparison.OrdinalIgnoreCase);
            var isAccessViolationCrash = data.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase);
            
            if (isBlenderCrash || isAccessViolationCrash)
            {
                RaiseErrorReceived($"Error: {e.Data}");
                return;
            }
            
            // 过滤掉Blender后台模式下的常见警告和第三方插件错误，这些不是真正的渲染失败
            var isNonCriticalWarning = data.Contains("GPU functions for drawing are not available in background mode") ||
                                     data.Contains("invalid non-printable character U+FEFF") ||
                                     data.Contains("SystemError") ||
                                     data.Contains("SyntaxError") ||
                                     data.Contains("Traceback") ||
                                     data.Contains("Warning") ||
                                     data.Contains("DeprecationWarning");
            
            if (isNonCriticalWarning)
            {
                // 将非关键警告作为普通输出处理
                RaiseOutputReceived($"[INFO] {e.Data}");
                return;
            }
            
            // 其他情况作为警告处理
            RaiseOutputReceived($"[WARN] {e.Data}");
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

    /// <summary>
    /// 执行查询脚本并自动释放进程
    /// </summary>
    public async Task<T> ExecuteQueryAndDisposeAsync<T>(
        string script,
        string operationName,
        Func<string, T> resultParser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ExecuteScript(script, operationName, cancellationToken);
            return resultParser(result.Output);
        }
        finally
        {
            // 查询完成后自动释放进程
            await StopAsync();
        }
    }
}
