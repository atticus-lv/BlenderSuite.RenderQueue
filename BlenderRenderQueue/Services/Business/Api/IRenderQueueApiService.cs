using System;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.Business.Api;

public interface IRenderQueueApiService
{
    Task StartAsync(int port = 8325);


    Task StopAsync();

    bool IsRunning { get; }

    int Port { get; }


    event EventHandler<ApiServiceStatusChangedEventArgs>? StatusChanged;
}

public class ApiServiceStatusChangedEventArgs(bool isRunning, int port, string message) : EventArgs
{
    public bool IsRunning { get; } = isRunning;
    public int Port { get; } = port;
    public string Message { get; } = message;
}