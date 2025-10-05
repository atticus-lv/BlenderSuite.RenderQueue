using System;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.BlenderService.BlenderProcess;

namespace BlenderRenderQueue.Services.Business.BlenderService;

/// <summary>
/// Blender进程服务 - 简化进程管理的服务包装器
/// </summary>
public class BlenderProcessService : IDisposable
{
    private readonly BlenderProcessManager _processManager;
    private readonly string _blenderPath;
    private bool _disposed;

    public BlenderProcessService(string blenderPath)
    {
        _blenderPath = blenderPath;
        _processManager = new BlenderProcessManager();
        
        // 订阅进程事件
        _processManager.ProcessCreated += OnProcessCreated;
        _processManager.ProcessDestroyed += OnProcessDestroyed;
        
        Console.WriteLine($"[BlenderProcessService] Service created for path: {_blenderPath}");
    }

    /// <summary>
    /// 执行查询操作（自动创建和释放查询进程）
    /// </summary>
    public async Task<T> ExecuteQueryAsync<T>(
        string script,
        string operationName,
        Func<string, T> resultParser,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[BlenderProcessService] Executing query: {operationName}");
        
        var process = await _processManager.CreateQueryProcessAsync(_blenderPath, cancellationToken);
        
        try
        {
            var result = await process.ExecuteScriptAsync(script, cancellationToken);
            return resultParser(result);
        }
        finally
        {
            await process.StopAsync();
            _processManager.UnregisterProcess(process.ProcessId);
            process.Dispose();
            Console.WriteLine($"[BlenderProcessService] Query completed: {operationName}");
        }
    }

    /// <summary>
    /// 执行渲染操作（创建渲染进程，需要手动管理生命周期）
    /// </summary>
    public async Task<IBlenderProcess> CreateRenderProcessAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[BlenderProcessService] Creating render process");
        return await _processManager.CreateRenderProcessAsync(_blenderPath, cancellationToken);
    }

    /// <summary>
    /// 执行视频生成操作（创建视频进程，需要手动管理生命周期）
    /// </summary>
    public async Task<IBlenderProcess> CreateVideoProcessAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[BlenderProcessService] Creating video process");
        return await _processManager.CreateVideoProcessAsync(_blenderPath, cancellationToken);
    }

    /// <summary>
    /// 停止所有进程
    /// </summary>
    public async Task StopAllProcessesAsync()
    {
        Console.WriteLine($"[BlenderProcessService] Stopping all processes");
        await _processManager.StopAllProcessesAsync();
    }

    /// <summary>
    /// 停止指定类型的进程
    /// </summary>
    public async Task StopProcessesByTypeAsync(BlenderProcessType processType)
    {
        Console.WriteLine($"[BlenderProcessService] Stopping processes of type: {processType}");
        await _processManager.StopProcessesByTypeAsync(processType);
    }

    /// <summary>
    /// 获取进程统计信息
    /// </summary>
    public BlenderProcessStats GetProcessStats()
    {
        return _processManager.GetProcessStats();
    }

    /// <summary>
    /// 获取指定类型的进程
    /// </summary>
    public System.Collections.Generic.IEnumerable<IBlenderProcess> GetProcessesByType(BlenderProcessType processType)
    {
        return _processManager.GetProcessesByType(processType);
    }

    /// <summary>
    /// 注销进程
    /// </summary>
    public void UnregisterProcess(string processId)
    {
        _processManager.UnregisterProcess(processId);
    }

    private void OnProcessCreated(IBlenderProcess process)
    {
        Console.WriteLine($"[BlenderProcessService] Process created - ID: {process.ProcessId}, Type: {process.ProcessType}");
    }

    private void OnProcessDestroyed(IBlenderProcess process)
    {
        Console.WriteLine($"[BlenderProcessService] Process destroyed - ID: {process.ProcessId}, Type: {process.ProcessType}");
    }

    public void Dispose()
    {
        if (_disposed) return;

        Console.WriteLine($"[BlenderProcessService] Disposing service");

        try
        {
            _processManager?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlenderProcessService] Error disposing service: {ex.Message}");
        }
        finally
        {
            _disposed = true;
            Console.WriteLine($"[BlenderProcessService] Service disposed");
        }
    }
}
