using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Services.Application.Logging;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.BlenderProcess;

/// <summary>
/// Blender进程管理器 - 统一管理所有Blender进程
/// </summary>
public class BlenderProcessManager : IDisposable
{
    private readonly ConcurrentDictionary<string, IBlenderProcess> _activeProcesses = new();
    private readonly object _lock = new();
    private readonly IRenderLogService? _logService;
    private bool _disposed;

    public BlenderProcessManager(IRenderLogService? logService = null)
    {
        _logService = logService;
    }

    /// <summary>
    /// 进程创建事件
    /// </summary>
    public event Action<IBlenderProcess>? ProcessCreated;
    
    /// <summary>
    /// 进程销毁事件
    /// </summary>
    public event Action<IBlenderProcess>? ProcessDestroyed;
    
    /// <summary>
    /// 所有活跃进程
    /// </summary>
    public IReadOnlyCollection<IBlenderProcess> ActiveProcesses => _activeProcesses.Values.ToList();

    /// <summary>
    /// 创建查询进程
    /// </summary>
    public async Task<IBlenderProcess> CreateQueryProcessAsync(string blenderPath, CancellationToken cancellationToken = default)
    {
        var process = new BlenderQueryProcess(blenderPath, _logService);
        await RegisterProcessAsync(process, cancellationToken);
        return process;
    }

    /// <summary>
    /// 创建渲染进程
    /// </summary>
    public async Task<IBlenderProcess> CreateRenderProcessAsync(string blenderPath, CancellationToken cancellationToken = default)
    {
        var process = new BlenderRenderProcess(blenderPath, _logService);
        await RegisterProcessAsync(process, cancellationToken);
        return process;
    }

    /// <summary>
    /// 创建视频生成进程
    /// </summary>
    public async Task<IBlenderProcess> CreateVideoProcessAsync(string blenderPath, CancellationToken cancellationToken = default)
    {
        var process = new BlenderVideoProcess(blenderPath, _logService);
        await RegisterProcessAsync(process, cancellationToken);
        return process;
    }

    /// <summary>
    /// 注册进程
    /// </summary>
    private async Task RegisterProcessAsync(IBlenderProcess process, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderProcessManager));

        _activeProcesses.TryAdd(process.ProcessId, process);
        
        // 订阅进程退出事件，自动清理
        process.OnProcessExited += (exitCode) => UnregisterProcess(process.ProcessId);
        
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Process registered - ID: {process.ProcessId}, Type: {process.ProcessType}", source: "BlenderProcessManager");
        
        // 启动进程
        await process.StartAsync(cancellationToken);
        
        ProcessCreated?.Invoke(process);
    }

    /// <summary>
    /// 注销进程
    /// </summary>
    public void UnregisterProcess(string processId)
    {
        if (_activeProcesses.TryRemove(processId, out var process))
        {
            _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Process unregistered - ID: {processId}, Type: {process.ProcessType}", source: "BlenderProcessManager");
            ProcessDestroyed?.Invoke(process);
        }
    }

    /// <summary>
    /// 获取指定类型的进程
    /// </summary>
    public IEnumerable<IBlenderProcess> GetProcessesByType(BlenderProcessType processType)
    {
        return _activeProcesses.Values.Where(p => p.ProcessType == processType);
    }

    /// <summary>
    /// 获取指定ID的进程
    /// </summary>
    public IBlenderProcess? GetProcess(string processId)
    {
        _activeProcesses.TryGetValue(processId, out var process);
        return process;
    }

    /// <summary>
    /// 停止所有进程
    /// </summary>
    public async Task StopAllProcessesAsync()
    {
        var tasks = _activeProcesses.Values.Select(async process =>
        {
            try
            {
                await process.StopAsync();
            }
            catch (Exception ex)
            {
                _logService?.Write(RenderLogLevel.Error, RenderLogScope.Worker, $"Error stopping process {process.ProcessId}: {ex.Message}", source: "BlenderProcessManager");
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 停止指定类型的进程
    /// </summary>
    public async Task StopProcessesByTypeAsync(BlenderProcessType processType)
    {
        var processes = GetProcessesByType(processType).ToList();
        var tasks = processes.Select(async process =>
        {
            try
            {
                await process.StopAsync();
            }
            catch (Exception ex)
            {
                _logService?.Write(RenderLogLevel.Error, RenderLogScope.Worker, $"Error stopping process {process.ProcessId}: {ex.Message}", source: "BlenderProcessManager");
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 获取进程统计信息
    /// </summary>
    public BlenderProcessStats GetProcessStats()
    {
        var processes = _activeProcesses.Values.ToList();
        return new BlenderProcessStats
        {
            TotalProcesses = processes.Count,
            QueryProcesses = processes.Count(p => p.ProcessType == BlenderProcessType.Query),
            RenderProcesses = processes.Count(p => p.ProcessType == BlenderProcessType.Render),
            VideoProcesses = processes.Count(p => p.ProcessType == BlenderProcessType.Video),
            RunningProcesses = processes.Count(p => p.IsRunning),
            DisposedProcesses = processes.Count(p => p.IsDisposed)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, "Disposing process manager...", source: "BlenderProcessManager");

        // 停止所有进程（在线程池上执行并限时等待，避免在 UI 线程上 sync-over-async 死锁）
        try
        {
            if (!Task.Run(StopAllProcessesAsync).Wait(TimeSpan.FromSeconds(10)))
            {
                _logService?.Write(RenderLogLevel.Warning, RenderLogScope.Worker, "Timed out waiting for processes to stop during dispose", source: "BlenderProcessManager");
            }
        }
        catch (Exception ex)
        {
            _logService?.Write(RenderLogLevel.Error, RenderLogScope.Worker, $"Error stopping processes during dispose: {ex.Message}", source: "BlenderProcessManager");
        }

        // 清理所有进程
        foreach (var process in _activeProcesses.Values)
        {
            try
            {
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logService?.Write(RenderLogLevel.Error, RenderLogScope.Worker, $"Error disposing process {process.ProcessId}: {ex.Message}", source: "BlenderProcessManager");
            }
        }

        _activeProcesses.Clear();
        _disposed = true;

        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, "Process manager disposed", source: "BlenderProcessManager");
    }
}

/// <summary>
/// Blender进程统计信息
/// </summary>
public class BlenderProcessStats
{
    public int TotalProcesses { get; set; }
    public int QueryProcesses { get; set; }
    public int RenderProcesses { get; set; }
    public int VideoProcesses { get; set; }
    public int RunningProcesses { get; set; }
    public int DisposedProcesses { get; set; }
}
