using System;
using System.Collections.Generic;

namespace BlenderSuite.RenderQueue.Services.Application.Queue;

public sealed class RenderQueueSnapshot
{
    public QueueExecutionState State { get; init; }
    public Guid? CurrentTaskId { get; init; }
    public int ActiveTaskCount { get; init; }
    public int CompletedTaskCount { get; init; }
    public int FailedTaskCount { get; init; }
    public int TotalFrames { get; init; }
    public double CompletedFrameProgress { get; init; }
    public double OverallProgress01 { get; init; }
    public string QueueStatusText { get; init; } = "Queue_Idle";
    public string RemainingTimeText { get; init; } = string.Empty;
    public bool AutoStartNext { get; init; }
    public BlenderSuite.RenderQueue.ViewModels.PostRenderBehavior PostRenderBehavior { get; init; }
    public bool CanStartQueue { get; init; }
    public bool CanStopQueue { get; init; }
    public bool CanPauseQueue { get; init; }
    public bool CanResumeQueue { get; init; }
    public bool CanClearTasks { get; init; }
    public IReadOnlyList<RenderTaskSnapshot> Tasks { get; init; } = Array.Empty<RenderTaskSnapshot>();
}
