using System;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Api.Models;

/// <summary>
/// 队列状态响应模型
/// </summary>
public class QueueStatusResponse
{
    public DateTime Timestamp { get; set; }

    public QueueState QueueState { get; set; }


    public int ActiveTaskCount { get; set; }


    public int CompletedTaskCount { get; set; }


    public int FailedTaskCount { get; set; }

    public int TotalFrames { get; set; }

    public int CompletedFrames { get; set; }

    public double OverallProgress { get; set; }

    public string RemainingTime { get; set; } = string.Empty;

    public CurrentTaskInfo? CurrentTask { get; set; }
}

public class CurrentTaskInfo
{
    public string FileName { get; set; } = string.Empty;

    public int CurrentFrame { get; set; }

    public int TotalFrames { get; set; }

    public double Progress { get; set; }

    public RenderTaskStatus Status { get; set; }

    public RenderEngine Engine { get; set; }

    public string SampleText { get; set; } = string.Empty;

    public string SavedPath { get; set; } = string.Empty;
}