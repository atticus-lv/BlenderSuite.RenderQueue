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
    public string? RenderEngine { get; set; }

    public string? RenderEngineDisplayName => RenderEngine switch
    {
        "CYCLES" => "Cycles",
        "BLENDER_EEVEE" => "Eevee",
        "BLENDER_WORKBENCH" => "Workbench",
        _ => RenderEngine
    };

    public string? SceneName { get; set; }
    public double? Fps { get; set; }
    public string? FramePath { get; set; }
    public int TotalFrames => Math.Max(0, FrameEnd - FrameStart + 1);
    public bool IsLoaded => !string.IsNullOrEmpty(FilePath);
    public string FileName => IsLoaded ? Path.GetFileName(FilePath) : string.Empty;

    /// <summary>
    /// 从另一个BlendFileProperties对象加载属性
    /// </summary>
    public void LoadFrom(BlendFileProperties source)
    {
        FilePath = source.FilePath;
        FrameStart = source.FrameStart;
        FrameEnd = source.FrameEnd;
        CameraName = source.CameraName;
        RenderOutputPath = source.RenderOutputPath;
        RenderOutputFormat = source.RenderOutputFormat;
        RenderEngine = source.RenderEngine;
        SceneName = source.SceneName;
        Fps = source.Fps;
        FramePath = source.FramePath;
    }
}