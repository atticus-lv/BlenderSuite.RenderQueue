using System;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.Business.Api;

/// <summary>
/// 渲染队列API服务接口
/// </summary>
public interface IRenderQueueApiService
{
    /// <summary>
    /// 启动API服务
    /// </summary>
    /// <param name="port">端口号，默认为8080</param>
    /// <returns></returns>
    Task StartAsync(int port = 8325);

    /// <summary>
    /// 停止API服务
    /// </summary>
    /// <returns></returns>
    Task StopAsync();

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 当前端口号
    /// </summary>
    int Port { get; }

    /// <summary>
    /// API服务状态变化事件
    /// </summary>
    event EventHandler<ApiServiceStatusChangedEventArgs>? StatusChanged;
}

/// <summary>
/// API服务状态变化事件参数
/// </summary>
public class ApiServiceStatusChangedEventArgs : EventArgs
{
    public bool IsRunning { get; }
    public int Port { get; }
    public string Message { get; }

    public ApiServiceStatusChangedEventArgs(bool isRunning, int port, string message)
    {
        IsRunning = isRunning;
        Port = port;
        Message = message;
    }
}
