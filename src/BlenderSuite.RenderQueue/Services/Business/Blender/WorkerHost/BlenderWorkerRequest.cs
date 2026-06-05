namespace BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;

public sealed class BlenderWorkerRequest
{
    public required string BlendFilePath { get; init; }
    public bool Animation { get; init; }
    public int? FrameStart { get; init; }
    public int? FrameEnd { get; init; }
    public int? SingleFrame { get; init; }
    public string? SceneName { get; init; }
    public string? OutputPath { get; init; }
}
