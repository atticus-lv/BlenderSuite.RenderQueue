using System;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Api.Models;

/// <summary>
/// 基础任务信息模型 - 包含所有任务共有的属性
/// </summary>
public abstract class BaseTaskInfo
{
    /// <summary>
    /// 任务ID（使用HashCode）
    /// </summary>
    public int TaskId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 任务状态
    /// </summary>
    public RenderTaskStatus Status { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 开始帧
    /// </summary>
    public int StartFrame { get; set; }

    /// <summary>
    /// 结束帧
    /// </summary>
    public int EndFrame { get; set; }

    /// <summary>
    /// 当前帧
    /// </summary>
    public int CurrentFrame { get; set; }

    /// <summary>
    /// 总帧数
    /// </summary>
    public int TotalFrames { get; set; }

    /// <summary>
    /// 整体进度 (0-1)
    /// </summary>
    public double OverallProgress { get; set; }

    /// <summary>
    /// 当前帧内进度 (0-1)
    /// </summary>
    public double CurrentFrameProgress { get; set; }

    /// <summary>
    /// 场景名称
    /// </summary>
    public string SceneName { get; set; } = string.Empty;

    /// <summary>
    /// 是否覆写帧范围
    /// </summary>
    public bool OverrideFrameRange { get; set; }

    /// <summary>
    /// 是否覆写场景
    /// </summary>
    public bool OverrideScene { get; set; }

    /// <summary>
    /// 渲染引擎
    /// </summary>
    public RenderEngine Engine { get; set; }

    /// <summary>
    /// 采样信息文本
    /// </summary>
    public string SampleText { get; set; } = string.Empty;

    /// <summary>
    /// 保存路径
    /// </summary>
    public string SavedPath { get; set; } = string.Empty;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdateTime { get; set; }
}
