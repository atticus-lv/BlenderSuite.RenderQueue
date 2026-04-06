namespace BlenderRenderQueue.Services.Application.Queue;

public enum RenderTaskExecutionState
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}
