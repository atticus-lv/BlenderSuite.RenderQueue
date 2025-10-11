using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Api.Models;

/// <summary>
/// 优化的队列状态响应 - 包含完整的任务静态信息
/// </summary>
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

    /// <summary>
    /// 所有任务的完整静态信息
    /// </summary>
    [JsonPropertyName("tasks")]
    public List<OptimizedTaskInfo> Tasks { get; set; } = new();
}

/// <summary>
/// 优化的任务信息 - 包含完整的静态信息
/// </summary>
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

    /// <summary>
    /// 当前帧（动态数据，但包含在队列状态中）
    /// </summary>
    [JsonPropertyName("currentFrame")]
    public int CurrentFrame { get; set; }
}
