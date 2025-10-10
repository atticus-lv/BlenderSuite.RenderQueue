using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlenderSuite.RenderQueue.Models;


public enum QueueState
{
    Idle,
    Running,
    Paused,
    Completed,
    Error
}

public enum RenderTaskStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum RenderEngine
{
    Unknown,
    Cycles,
    Eevee,
    Workbench
}

// 优化的API响应模型 - 与服务器端保持一致
public class OptimizedQueueStatusResponse
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("queueState")]
    public QueueState QueueState { get; set; }

    [JsonPropertyName("activeTaskCount")]
    public int ActiveTaskCount { get; set; }

    [JsonPropertyName("completedTaskCount")]
    public int CompletedTaskCount { get; set; }

    [JsonPropertyName("failedTaskCount")]
    public int FailedTaskCount { get; set; }

    [JsonPropertyName("totalFrames")]
    public int TotalFrames { get; set; }

    [JsonPropertyName("completedFrames")]
    public int CompletedFrames { get; set; }

    [JsonPropertyName("overallProgress")]
    public double OverallProgress { get; set; }

    [JsonPropertyName("remainingTime")]
    public string RemainingTime { get; set; } = string.Empty;

    [JsonPropertyName("tasks")]
    public List<OptimizedTaskInfo> Tasks { get; set; } = new();
}

public class CurrentTaskInfo
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("currentFrame")]
    public int CurrentFrame { get; set; }

    [JsonPropertyName("totalFrames")]
    public int TotalFrames { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("status")]
    public RenderTaskStatus Status { get; set; }

    [JsonPropertyName("engine")]
    public RenderEngine Engine { get; set; }

    [JsonPropertyName("sampleText")]
    public string SampleText { get; set; } = string.Empty;

    [JsonPropertyName("savedPath")]
    public string SavedPath { get; set; } = string.Empty;
}

public class OptimizedTaskInfo
{
    [JsonPropertyName("taskId")]
    public int TaskId { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public RenderTaskStatus Status { get; set; }

    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("startFrame")]
    public int StartFrame { get; set; }

    [JsonPropertyName("endFrame")]
    public int EndFrame { get; set; }

    [JsonPropertyName("currentFrame")]
    public int CurrentFrame { get; set; }

    [JsonPropertyName("totalFrames")]
    public int TotalFrames { get; set; }

    [JsonPropertyName("overallProgress")]
    public double OverallProgress { get; set; }

    [JsonPropertyName("currentFrameProgress")]
    public double CurrentFrameProgress { get; set; }

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = string.Empty;

    [JsonPropertyName("overrideFrameRange")]
    public bool OverrideFrameRange { get; set; }

    [JsonPropertyName("overrideScene")]
    public bool OverrideScene { get; set; }

    [JsonPropertyName("engine")]
    public RenderEngine Engine { get; set; }

    [JsonPropertyName("sampleTotal")]
    public int? SampleTotal { get; set; }

    [JsonPropertyName("savedPath")]
    public string SavedPath { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdateTime")]
    public DateTime LastUpdateTime { get; set; }
}

// 优化的进度更新模型
public class OptimizedProgressUpdate
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("currentTask")]
    public CurrentTaskProgress? CurrentTask { get; set; }

    [JsonPropertyName("statusChanges")]
    public List<TaskStatusChange>? StatusChanges { get; set; }
}

public class CurrentTaskProgress
{
    [JsonPropertyName("taskId")]
    public int TaskId { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("currentFrame")]
    public int CurrentFrame { get; set; }

    [JsonPropertyName("overallProgress")]
    public double OverallProgress { get; set; }

    [JsonPropertyName("currentFrameProgress")]
    public double CurrentFrameProgress { get; set; }

    [JsonPropertyName("status")]
    public RenderTaskStatus Status { get; set; }

    [JsonPropertyName("realtimeProgress")]
    public RealtimeRenderProgress RealtimeProgress { get; set; } = new();
}

public class RealtimeRenderProgress
{
    [JsonPropertyName("currentFrame")]
    public int CurrentFrame { get; init; }

    [JsonPropertyName("sampleCurrent")]
    public int? SampleCurrent { get; init; }

    [JsonPropertyName("memoryMB")]
    public double? MemoryMB { get; init; }

    [JsonPropertyName("elapsed")]
    public TimeSpan? Elapsed { get; init; }

    [JsonPropertyName("savedPath")]
    public string? SavedPath { get; init; }
}

public class TaskStatusChange
{
    [JsonPropertyName("taskId")]
    public int TaskId { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public RenderTaskStatus Status { get; set; }

    [JsonPropertyName("overallProgress")]
    public double OverallProgress { get; set; }
}

public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}
