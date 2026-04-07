namespace BlenderRenderQueue.Services.Application.Queue;

public enum QueueExecutionState
{
    Idle,
    Running,
    Paused,
    Completed,
    Error
}
