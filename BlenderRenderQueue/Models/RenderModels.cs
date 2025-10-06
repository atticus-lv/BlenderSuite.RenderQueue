using System;

namespace BlenderRenderQueue.Models;

public enum RenderTaskStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum QueueState
{
    Idle,
    Running,
    Paused,
    Completed,
    Error
}

public enum RenderEngine
{
    Unknown,
    Cycles,
    Eevee,
    Workbench
}

public static class RenderTaskStatusExtensions
{
    public static string GetLocalizationKey(this RenderTaskStatus status)
    {
        return status switch
        {
            RenderTaskStatus.Pending => "TaskStatus_Pending",
            RenderTaskStatus.Running => "TaskStatus_Running",
            RenderTaskStatus.Paused => "TaskStatus_Paused",
            RenderTaskStatus.Completed => "TaskStatus_Completed",
            RenderTaskStatus.Failed => "TaskStatus_Failed",
            RenderTaskStatus.Cancelled => "TaskStatus_Cancelled",
            _ => "TaskStatus_Unknown"
        };
    }
}

public static class QueueStateExtensions
{
    public static string GetLocalizationKey(this QueueState state)
    {
        return state switch
        {
            QueueState.Idle => "Queue_Idle",
            QueueState.Running => "Queue_Running",
            QueueState.Paused => "Queue_Paused",
            QueueState.Completed => "Queue_Completed",
            QueueState.Error => "Queue_Error",
            _ => "Queue_Unknown"
        };
    }
}

public sealed record RenderProgress
{
    public int CurrentFrame { get; init; }
    public int? StartFrame { get; init; }
    public int? EndFrame { get; init; }
    public int? SampleCurrent { get; init; }
    public int? SampleTotal { get; init; }
    public RenderEngine Engine { get; init; }
    public double? MemoryMB { get; init; }
    public string? Scene { get; init; }
    public string? ViewLayer { get; init; }
    public string? SavedPath { get; init; }
    public TimeSpan? Elapsed { get; init; }
}

public abstract record RenderEvent;

public record RenderSessionStarted(bool IsAnimation, int? StartFrame, int? EndFrame) : RenderEvent;

public record RenderStarted(int Frame, string? Scene, string? ViewLayer, RenderEngine Engine) : RenderEvent;

public record RenderProgressEvent(RenderProgress Progress) : RenderEvent;

public record RenderSaved(string Path, int Frame) : RenderEvent;

public record RenderCompletedFrame(int Frame, TimeSpan Time, TimeSpan? Saving) : RenderEvent;

public record RenderCompletedAll(TimeSpan? TotalTime = null) : RenderEvent;

public record RenderOutput(string Line) : RenderEvent;

public record RenderError(string Message) : RenderEvent;