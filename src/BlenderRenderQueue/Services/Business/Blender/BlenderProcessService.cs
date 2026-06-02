using System;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Business.Blender.BlenderProcess;

namespace BlenderRenderQueue.Services.Business.Blender;

/// <summary>
/// Blender进程服务 - 简化进程管理的服务包装器
/// </summary>
public class BlenderProcessService : IDisposable
{
    private readonly BlenderProcessManager _processManager;
    private readonly string _blenderPath;
    private readonly IRenderLogService? _logService;
    private bool _disposed;

    public BlenderProcessService(string blenderPath, IRenderLogService? logService = null)
    {
        _blenderPath = blenderPath;
        _logService = logService;
        _processManager = new BlenderProcessManager(logService);
        
        // 订阅进程事件
        _processManager.ProcessCreated += OnProcessCreated;
        _processManager.ProcessDestroyed += OnProcessDestroyed;
        
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Service created for path: {_blenderPath}", source: "BlenderProcessService");
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
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Executing query: {operationName}", source: "BlenderProcessService");
        
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
            _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Query completed: {operationName}", source: "BlenderProcessService");
        }
    }

    /// <summary>
    /// 执行渲染操作（创建渲染进程，需要手动管理生命周期）
    /// </summary>
    public async Task<IBlenderProcess> CreateRenderProcessAsync(CancellationToken cancellationToken = default)
    {
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Creating render process", source: "BlenderProcessService");
        return await _processManager.CreateRenderProcessAsync(_blenderPath, cancellationToken);
    }

    /// <summary>
    /// 执行视频生成操作（创建视频进程，需要手动管理生命周期）
    /// </summary>
    public async Task<IBlenderProcess> CreateVideoProcessAsync(CancellationToken cancellationToken = default)
    {
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Creating video process", source: "BlenderProcessService");
        return await _processManager.CreateVideoProcessAsync(_blenderPath, cancellationToken);
    }

    /// <summary>
    /// 停止所有进程
    /// </summary>
    public async Task StopAllProcessesAsync()
    {
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Stopping all processes", source: "BlenderProcessService");
        await _processManager.StopAllProcessesAsync();
    }

    /// <summary>
    /// 停止指定类型的进程
    /// </summary>
    public async Task StopProcessesByTypeAsync(BlenderProcessType processType)
    {
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Stopping processes of type: {processType}", source: "BlenderProcessService");
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
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Process created - ID: {process.ProcessId}, Type: {process.ProcessType}", source: "BlenderProcessService");
    }

    private void OnProcessDestroyed(IBlenderProcess process)
    {
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Process destroyed - ID: {process.ProcessId}, Type: {process.ProcessType}", source: "BlenderProcessService");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Disposing service", source: "BlenderProcessService");

        try
        {
            _processManager?.Dispose();
        }
        catch (Exception ex)
        {
            _logService?.Write(RenderLogLevel.Error, RenderLogScope.Worker, $"Error disposing service: {ex.Message}", source: "BlenderProcessService");
        }
        finally
        {
            _disposed = true;
            _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Service disposed", source: "BlenderProcessService");
        }
    }
}
