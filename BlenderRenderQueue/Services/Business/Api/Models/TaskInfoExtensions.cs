using System;
using System.IO;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Services.Business.Api.Models;

/// <summary>
/// 任务信息转换扩展方法
/// </summary>
public static class TaskInfoExtensions
{
    /// <summary>
    /// 将RenderTaskViewModel转换为TaskInfoResponse
    /// </summary>
    public static TaskInfoResponse ToApiResponse(this RenderTaskViewModel task)
    {
        return new TaskInfoResponse
        {
            TaskId = task.GetHashCode(),
            FileName = Path.GetFileName(task.BlendFilePath),
            FilePath = task.BlendFilePath,
            Status = task.Status,
            Enable = task.Enable,
            IsValid = task.IsValid,
            StartFrame = task.StartFrame,
            EndFrame = task.EndFrame,
            CurrentFrame = task.CurrentFrame,
            TotalFrames = task.RealTotalFrames,
            OverallProgress = task.OverallProgress01,
            CurrentFrameProgress = task.Progress01,
            SceneName = task.SelectedSceneName ?? string.Empty,
            OverrideFrameRange = task.OverrideFrameRange,
            OverrideScene = task.OverrideScene,
            Engine = ParseRenderEngine(task.Engine),
            SampleText = task.SampleText,
            SavedPath = task.SavedPath,
            LastUpdateTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 将RenderTaskViewModel转换为ProgressUpdate
    /// </summary>
    public static ProgressUpdate ToProgressUpdate(this RenderTaskViewModel task)
    {
        return new ProgressUpdate
        {
            TaskId = task.GetHashCode(),
            Timestamp = DateTime.UtcNow,
            FileName = Path.GetFileName(task.BlendFilePath),
            Status = task.Status,
            Progress = new RenderProgress
            {
                CurrentFrame = task.CurrentFrame,
                StartFrame = task.StartFrame,
                EndFrame = task.EndFrame,
                Engine = ParseRenderEngine(task.Engine),
                Scene = task.SelectedSceneName,
                SavedPath = task.SavedPath
            },
            OverallProgress = task.OverallProgress01,
            CurrentFrameProgress = task.Progress01
        };
    }

    /// <summary>
    /// 将RenderTaskViewModel转换为CurrentTaskInfo
    /// </summary>
    public static CurrentTaskInfo ToCurrentTaskInfo(this RenderTaskViewModel task)
    {
        return new CurrentTaskInfo
        {
            FileName = Path.GetFileName(task.BlendFilePath),
            CurrentFrame = task.CurrentFrame,
            TotalFrames = task.RealTotalFrames,
            Progress = task.OverallProgress01,
            Status = task.Status,
            Engine = ParseRenderEngine(task.Engine),
            SampleText = task.SampleText,
            SavedPath = task.SavedPath
        };
    }

    /// <summary>
    /// 解析渲染引擎字符串为枚举
    /// </summary>
    private static RenderEngine ParseRenderEngine(string engine)
    {
        return engine?.ToUpper() switch
        {
            "CYCLES" => RenderEngine.Cycles,
            "BLENDER_EEVEE" or "EEVEE" => RenderEngine.Eevee,
            "BLENDER_WORKBENCH" or "WORKBENCH" => RenderEngine.Workbench,
            _ => RenderEngine.Unknown
        };
    }
}
