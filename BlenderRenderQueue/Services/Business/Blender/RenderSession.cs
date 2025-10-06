using System;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Blender.BlenderProcess;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser;

namespace BlenderRenderQueue.Services.Business.Blender;

public interface IRenderSession : IDisposable
{
    event Action<RenderEvent>? OnEvent;
    event Action<RenderProgress>? OnProgress;
    RenderProgress Latest { get; }
    void Cancel();
}

public sealed class RenderSession : IRenderSession
{
    private readonly IBlenderProcess _process;
    private readonly IRenderOutputParser _parser;
    private bool _disposed;

    public event Action<RenderEvent>? OnEvent;
    public event Action<RenderProgress>? OnProgress;

    public RenderProgress Latest => _parser.Current;

    public RenderSession(IBlenderProcess process, IRenderOutputParser parser)
    {
        _process = process;
        _parser = parser;
        _process.OnOutputReceived += HandleOutput;
        _process.OnErrorReceived += HandleError;
    }

    private void HandleOutput(string line)
    {
        var events = _parser.ParseLine(line);
        foreach (var e in events)
        {
            OnEvent?.Invoke(e);
            if (e is RenderProgressEvent pe)
            {
                OnProgress?.Invoke(pe.Progress);
            }
        }
    }

    private void HandleError(string msg)
    {
        OnEvent?.Invoke(new RenderError(msg));
    }

    public void Cancel()
    {
        try
        {
            // 使用 Task.Run 来避免死锁
            Task.Run(async () => await _process.StopAsync()).Wait(5000); // 5秒超时
        }
        catch
        {
            /* 交给上层决定是否处理异常 */
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 先取消事件订阅，避免在停止过程中触发事件
        _process.OnOutputReceived -= HandleOutput;
        _process.OnErrorReceived -= HandleError;

        // 然后取消渲染
        Cancel();

        _disposed = true;
    }
}