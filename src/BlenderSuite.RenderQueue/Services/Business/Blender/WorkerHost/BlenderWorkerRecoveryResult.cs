namespace BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;

public sealed class BlenderWorkerRecoveryResult
{
    public bool Recovered { get; init; }
    public string ReloadedFile { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
