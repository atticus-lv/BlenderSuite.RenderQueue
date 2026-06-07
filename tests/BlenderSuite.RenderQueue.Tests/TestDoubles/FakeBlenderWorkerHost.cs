using System;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;

namespace BlenderSuite.RenderQueue.Tests;

internal sealed class FakeBlenderWorkerHost : IBlenderWorkerHost
{
    public BlenderWorkerHostState State { get; } = new() { ProcessGeneration = 1, Status = "ready", IsProcessRunning = true };

    public event Action<string>? OnOutputReceived;
    public event Action<string>? OnErrorReceived;
    public event Action<int>? OnProcessExited;

    public int EnsureReadyCalls { get; private set; }
    public int RenderTaskCalls { get; private set; }
    public int RecoverCalls { get; private set; }
    public int CancelCalls { get; private set; }
    public int ShutdownCalls { get; private set; }
    public Func<BlenderWorkerRequest, CancellationToken, Task<BlenderWorkerResponse>>? RenderTaskHandler { get; set; }

    public Task EnsureReadyAsync(string blenderExecutablePath, CancellationToken cancellationToken = default)
    {
        EnsureReadyCalls++;
        State.BlenderExecutablePath = blenderExecutablePath;
        return Task.CompletedTask;
    }

    public Task<BlenderWorkerResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BlenderWorkerResponse { Ok = true, WorkerState = State.Status });
    }

    public Task<BlenderWorkerResponse> QueryFileInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BlenderWorkerResponse { Ok = true, WorkerState = State.Status });
    }

    public Task<BlenderWorkerResponse> LoadFileAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        State.CurrentFile = blendFilePath;
        return Task.FromResult(new BlenderWorkerResponse { Ok = true, WorkerState = State.Status, CurrentFile = blendFilePath });
    }

    public Task<BlenderWorkerResponse> RenderTaskAsync(BlenderWorkerRequest request, CancellationToken cancellationToken = default)
    {
        RenderTaskCalls++;
        State.IsRendering = true;
        State.Status = "rendering";
        if (RenderTaskHandler != null)
        {
            return RenderTaskHandler(request, cancellationToken);
        }

        return Task.FromResult(new BlenderWorkerResponse
        {
            Ok = true,
            WorkerState = "completed",
            OutputVerified = true
        });
    }

    public Task CancelCurrentRenderAsync(CancellationToken cancellationToken = default)
    {
        CancelCalls++;
        State.IsRendering = false;
        State.Status = "cancelled";
        return Task.CompletedTask;
    }

    public Task<BlenderWorkerRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        RecoverCalls++;
        State.ProcessGeneration++;
        State.IsProcessRunning = true;
        State.IsRendering = false;
        State.Status = "ready";
        State.LastError = string.Empty;
        State.LastErrorCategory = string.Empty;
        return Task.FromResult(new BlenderWorkerRecoveryResult
        {
            Recovered = true,
            Message = "Recovered fake worker."
        });
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        ShutdownCalls++;
        State.IsProcessRunning = false;
        State.Status = "stopped";
        return Task.CompletedTask;
    }

    public void EmitOutput(string line) => OnOutputReceived?.Invoke(line);
    public void EmitError(string line) => OnErrorReceived?.Invoke(line);
    public void EmitExit(int exitCode)
    {
        State.IsProcessRunning = false;
        State.IsRendering = false;
        OnProcessExited?.Invoke(exitCode);
    }

    public void Dispose()
    {
    }
}
