using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.Business.BlenderService.BlenderProcess;

/// <summary>
/// Blender进程接口
/// </summary>
public interface IBlenderProcess : IDisposable
{
    /// <summary>
    /// 进程唯一标识符
    /// </summary>
    string ProcessId { get; }
    
    /// <summary>
    /// 进程类型
    /// </summary>
    BlenderProcessType ProcessType { get; }
    
    /// <summary>
    /// Blender路径
    /// </summary>
    string BlenderPath { get; }
    
    /// <summary>
    /// 进程是否正在运行
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// 进程是否已释放
    /// </summary>
    bool IsDisposed { get; }
    
    /// <summary>
    /// 输出接收事件
    /// </summary>
    event Action<string>? OnOutputReceived;
    
    /// <summary>
    /// 错误接收事件
    /// </summary>
    event Action<string>? OnErrorReceived;
    
    /// <summary>
    /// 进程退出事件
    /// </summary>
    event Action<int>? OnProcessExited;
    
    /// <summary>
    /// 启动进程
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 停止进程
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// 执行脚本
    /// </summary>
    Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default);
}

/// <summary>
/// Blender进程类型
/// </summary>
public enum BlenderProcessType
{
    /// <summary>
    /// 查询进程 - 用于查询文件属性、版本信息等
    /// </summary>
    Query,
    
    /// <summary>
    /// 渲染进程 - 用于渲染任务
    /// </summary>
    Render,
    
    /// <summary>
    /// 视频生成进程 - 用于生成视频
    /// </summary>
    Video
}
