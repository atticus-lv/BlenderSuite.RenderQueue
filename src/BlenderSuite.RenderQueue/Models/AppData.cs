using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlenderSuite.RenderQueue.Models;

/// <summary>
/// 应用程序数据模型 - 只包含渲染队列数据
/// </summary>
public class AppData
{
    [JsonPropertyName("Software")]
    public string Software { get; init; } = "BlenderSuite.RenderQueue";

    [JsonPropertyName("Version")]
    public string Version { get; init; } = "0.0.1";

    [JsonPropertyName("RenderQueue")]
    public List<RenderTaskData> RenderQueue { get; set; } = [];
}

public class RenderTaskData
{
    [JsonPropertyName("RenderTask")]
    public RenderTaskInfo RenderTask { get; set; } = new();
}

public class RenderTaskInfo
{
    [JsonPropertyName("Id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("Filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("Filepath")]
    public string Filepath { get; set; } = string.Empty;

    [JsonPropertyName("StartFrame")]
    public int StartFrame { get; set; } = 1;

    [JsonPropertyName("EndFrame")]
    public int EndFrame { get; set; } = 1;

    [JsonPropertyName("LastRenderedFrame")]
    public int LastRenderedFrame { get; set; } = 0;

    [JsonPropertyName("Enable")]
    public bool Enable { get; set; } = true;

    [JsonPropertyName("Override")]
    public OverrideData? Override { get; set; }
}

/// <summary>
/// 覆写数据
/// </summary>
public class OverrideData
{
    [JsonPropertyName("OverrideFrameRange")]
    public OverrideFrameRangeData? OverrideFrameRange { get; set; }

    [JsonPropertyName("OverrideScene")]
    public OverrideSceneData? OverrideScene { get; set; }
}

/// <summary>
/// 覆写帧范围数据
/// </summary>
public class OverrideFrameRangeData
{
    [JsonPropertyName("StartFrame")]
    public int StartFrame { get; set; } = 1;

    [JsonPropertyName("EndFrame")]
    public int EndFrame { get; set; } = 1;
}

/// <summary>
/// 覆写场景数据
/// </summary>
public class OverrideSceneData
{
    [JsonPropertyName("SceneName")]
    public string SceneName { get; set; } = string.Empty;
}