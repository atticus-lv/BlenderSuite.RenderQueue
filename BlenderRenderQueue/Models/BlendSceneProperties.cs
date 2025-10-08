using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlenderRenderQueue.Models;

/// <summary>
///     Blender文件属性信息
/// </summary>
public class BlendSceneProperties
{
    public string FilePath { get; set; } = string.Empty;
    public int FrameStart { get; set; }
    public int FrameEnd { get; set; }
    public int FrameCurrent { get; set; }
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
    public double? CyclesTimeLimit { get; set; }
    public List<string>? ReferencedScenes { get; set; }
    public List<string>? TimelineCameras { get; set; }
    public int TotalFrames => Math.Max(0, FrameEnd - FrameStart + 1);
    public bool IsLoaded => !string.IsNullOrEmpty(FilePath);
    public string FileName => IsLoaded ? Path.GetFileName(FilePath) : string.Empty;
    public bool IsCyclesEngine => RenderEngine == "CYCLES";
    public bool HasCyclesTimeLimit => IsCyclesEngine && CyclesTimeLimit.HasValue && CyclesTimeLimit.Value > 0;
    
    /// <summary>
    /// 场景类型显示文本
    /// </summary>
    public string SceneTypeDisplayText
    {
        get
        {
            if (ReferencedScenes == null || ReferencedScenes.Count == 0)
            {
                return "SceneType_Single";
            }
            else
            {
                var sceneNames = string.Join(", ", ReferencedScenes);
                return $"SceneType_Multi:{sceneNames}";
            }
        }
    }

    /// <summary>
    /// 是否为复合场景
    /// </summary>
    public bool IsCompositeScene => ReferencedScenes != null && ReferencedScenes.Count != 0;

    /// <summary>
    /// 相机类型显示文本
    /// </summary>
    public string CameraTypeDisplayText
    {
        get
        {
            if (TimelineCameras == null || !TimelineCameras.Any())
            {
                return "CameraType_Single";
            }
            else
            {
                var cameraNames = string.Join(", ", TimelineCameras);
                return $"CameraType_Timeline:{cameraNames}";
            }
        }
    }

    /// <summary>
    /// 是否为时间线相机场景
    /// </summary>
    public bool IsMultiCameraScene => TimelineCameras != null && TimelineCameras.Any();


}