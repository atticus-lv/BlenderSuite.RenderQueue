using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;

public interface IBlenderWorkerHost : IDisposable
{
    BlenderWorkerHostState State { get; }
    event Action<string>? OnOutputReceived;
    event Action<string>? OnErrorReceived;
    event Action<int>? OnProcessExited;

    Task EnsureReadyAsync(string blenderExecutablePath, CancellationToken cancellationToken = default);
    Task<BlenderWorkerResponse> PingAsync(CancellationToken cancellationToken = default);
    Task<BlenderWorkerResponse> QueryFileInfoAsync(CancellationToken cancellationToken = default);
    Task<BlenderWorkerResponse> LoadFileAsync(string blendFilePath, CancellationToken cancellationToken = default);
    Task<BlenderWorkerResponse> RenderTaskAsync(BlenderWorkerRequest request, CancellationToken cancellationToken = default);
    Task CancelCurrentRenderAsync(CancellationToken cancellationToken = default);
    Task<BlenderWorkerRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
