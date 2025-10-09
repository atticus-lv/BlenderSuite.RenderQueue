using System;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Api.Models;

public class ProgressUpdate
{
    public int TaskId { get; set; }

    public DateTime Timestamp { get; set; }

    public string FileName { get; set; } = string.Empty;

    public RenderTaskStatus Status { get; set; }

    public RenderProgress Progress { get; set; } = new();

    public double OverallProgress { get; set; }

    public double CurrentFrameProgress { get; set; }
}