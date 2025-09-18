using System;
using System.IO;

namespace BlenderRenderQueue.Models;

/// <summary>
///     Blender文件属性信息
/// </summary>
public class BlendFileProperties
{
    public string FilePath { get; set; } = string.Empty;
    public int FrameStart { get; set; }
    public int FrameEnd { get; set; }
    public string? CameraName { get; set; }
    public string? RenderOutputPath { get; set; }
    public string? RenderOutputFormat { get; set; }
    public int TotalFrames => Math.Max(0, FrameEnd - FrameStart + 1);
    public bool IsLoaded => !string.IsNullOrEmpty(FilePath);
    public string FileName => IsLoaded ? Path.GetFileName(FilePath) : string.Empty;
}