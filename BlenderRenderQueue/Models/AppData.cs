using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Models;

/// <summary>
/// 应用程序数据模型
/// </summary>
public class AppData
{
    [JsonPropertyName("Software")]
    public string Software { get; set; } = "BlenderRenderQueue";

    [JsonPropertyName("Version")]
    public string Version { get; set; } = "0.0.1";

    [JsonPropertyName("Settings")]
    public SettingsData Settings { get; set; } = new();

    [JsonPropertyName("RenderQueue")]
    public List<RenderTaskData> RenderQueue { get; set; } = new();
}

/// <summary>
/// 设置数据模型
/// </summary>
public class SettingsData
{
    [JsonPropertyName("BlenderPath")]
    public string BlenderPath { get; set; } = string.Empty;

    [JsonPropertyName("FfmpegPath")]
    public string FfmpegPath { get; set; } = string.Empty;
}

/// <summary>
/// 渲染任务数据模型
/// </summary>
public class RenderTaskData
{
    [JsonPropertyName("RenderTask")]
    public RenderTaskInfo RenderTask { get; set; } = new();
}

/// <summary>
/// 渲染任务信息
/// </summary>
public class RenderTaskInfo
{
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
