using System;
using System.Text.Json.Serialization;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Api.Models;

/// <summary>
/// 队列状态响应模型
/// </summary>
public class QueueStatusResponse
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

    [JsonPropertyName("currentTask")]
    public CurrentTaskInfo? CurrentTask { get; set; }
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