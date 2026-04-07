namespace BlenderRenderQueue.Services.Business.Blender.WorkerHost;

public sealed class BlenderWorkerHeartbeatResult
{
    public bool IsHealthy { get; init; }
    public bool Recovered { get; init; }
    public string Message { get; init; } = string.Empty;
}
