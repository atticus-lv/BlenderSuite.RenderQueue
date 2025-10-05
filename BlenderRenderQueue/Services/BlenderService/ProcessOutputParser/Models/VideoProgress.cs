using System;

namespace BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Models;

/// <summary>
/// 视频进度信息
/// </summary>
public class VideoProgress
{
    public int CurrentFrame { get; set; }
    public int TotalFrames { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsCompleted { get; set; }
    public string? SavedPath { get; set; }
    public TimeSpan? Elapsed { get; set; }
}
