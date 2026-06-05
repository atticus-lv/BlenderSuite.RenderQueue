using System;

namespace BlenderSuite.RenderQueue.Services.Application.Queue;

public sealed class RenderTaskSnapshot
{
    public Guid TaskId { get; init; }
    public string BlendFilePath { get; init; } = string.Empty;
    public string BlendFileName { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public bool IsValid { get; init; }
    public RenderTaskExecutionState State { get; init; }
    public int CurrentFrame { get; init; }
    public int CompletedFrames { get; init; }
    public int TotalFrames { get; init; }
    public double CurrentFrameProgress01 { get; init; }
    public double OverallProgress01 { get; init; }
    public string SampleText { get; init; } = string.Empty;
    public string StatusDetailText { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string PreviewPath { get; init; } = string.Empty;
    public string OverrideSceneName { get; init; } = string.Empty;
    public bool OverrideFrameRange { get; init; }
    public int RealStartFrame { get; init; }
    public int RealEndFrame { get; init; }
    public TimeSpan? Duration { get; init; }
}
