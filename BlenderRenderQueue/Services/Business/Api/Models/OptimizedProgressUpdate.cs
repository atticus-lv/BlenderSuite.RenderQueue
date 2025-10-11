using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Api.Models;

/// <summary>
/// 优化的进度更新模型 - 只包含必要的数据
/// </summary>
public class OptimizedProgressUpdate
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 当前渲染任务的详细进度信息
    /// </summary>
    [JsonPropertyName("currentTask")]
    public CurrentTaskProgress? CurrentTask { get; set; }

    /// <summary>
    /// 其他任务的状态变化（只包含状态变化的任务）
    /// </summary>
    [JsonPropertyName("statusChanges")]
    public List<TaskStatusChange>? StatusChanges { get; set; }
}

/// <summary>
/// 当前任务进度信息 - 包含实时渲染数据
/// </summary>
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

    /// <summary>
    /// 实时渲染进度信息 - 只包含动态数据
    /// </summary>
    [JsonPropertyName("realtimeProgress")]
    public RealtimeRenderProgress RealtimeProgress { get; set; } = new();
}

/// <summary>
/// 实时渲染进度 - 只包含动态变化的数据
/// </summary>
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

/// <summary>
/// 任务状态变化 - 只包含状态变化的任务
/// </summary>
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
