using System;

namespace BlenderRenderQueue.Services.Business.Blender.WorkerHost;

public sealed class BlenderWorkerHostState
{
    public string Status { get; set; } = "stopped";
    public bool IsProcessRunning { get; set; }
    public bool IsRendering { get; set; }
    public string BlenderExecutablePath { get; set; } = string.Empty;
    public string CurrentFile { get; set; } = string.Empty;
    public string ActiveScene { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset? RenderStartedAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? LastOutputAt { get; set; }
    public int ConsecutiveHeartbeatFailures { get; set; }
}
