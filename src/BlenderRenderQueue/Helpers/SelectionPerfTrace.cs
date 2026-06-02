using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Helpers;

internal static class SelectionPerfTrace
{
    private static readonly ConcurrentDictionary<Guid, SelectionTraceState> ActiveSelections = new();

    public static void Begin(Guid taskId, string taskName, string details)
    {
        var state = new SelectionTraceState(taskId, taskName, Stopwatch.GetTimestamp());
        ActiveSelections[taskId] = state;
        Write(taskId, taskName, "Begin", details, 0);
    }

    public static void Mark(Guid taskId, string taskName, string stage, string details = "")
    {
        if (!ActiveSelections.TryGetValue(taskId, out var state))
        {
            Write(taskId, taskName, stage, $"{details} (no active trace)", 0);
            return;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(state.StartTimestamp).TotalMilliseconds;
        Write(taskId, taskName, stage, details, elapsedMs);
    }

    public static void End(Guid taskId, string taskName, string details = "")
    {
        if (ActiveSelections.TryRemove(taskId, out var state))
        {
            var elapsedMs = Stopwatch.GetElapsedTime(state.StartTimestamp).TotalMilliseconds;
            Write(taskId, taskName, "End", details, elapsedMs);
            return;
        }

        Write(taskId, taskName, "End", $"{details} (no active trace)", 0);
    }

    private static void Write(Guid taskId, string taskName, string stage, string details, double elapsedMs)
    {
        var message =
            $"[SelectionPerf] task={taskName} id={taskId:D} stage={stage} elapsed={elapsedMs:F1}ms {details}".TrimEnd();
        Debug.WriteLine(message);
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, message, "SelectionPerfTrace");
    }

    private sealed record SelectionTraceState(Guid TaskId, string TaskName, long StartTimestamp);
}
