using System.Collections.Generic;

namespace BlenderRenderQueue.Services.Business.Blender.WorkerHost;

public sealed class BlenderWorkerResponse
{
    public string RequestId { get; init; } = string.Empty;
    public bool Ok { get; init; }
    public string WorkerState { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string ErrorCategory { get; init; } = string.Empty;
    public string CurrentFile { get; init; } = string.Empty;
    public string ActiveScene { get; init; } = string.Empty;
    public IReadOnlyList<string> Scenes { get; init; } = [];
    public string Camera { get; init; } = string.Empty;
    public int FrameStart { get; init; }
    public int FrameEnd { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public bool IsSaved { get; init; }
    public bool OutputVerified { get; init; }
    public string RenderStartedAt { get; init; } = string.Empty;
    public string LastHeartbeatAt { get; init; } = string.Empty;
}
