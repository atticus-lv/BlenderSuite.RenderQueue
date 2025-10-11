using System;
using System.Collections.Generic;
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
    /// 将RenderTaskViewModel转换为OptimizedTaskInfo
    /// </summary>
    public static OptimizedTaskInfo ToOptimizedTaskInfo(this RenderTaskViewModel task)
    {
        return new OptimizedTaskInfo
        {
            TaskId = GetTaskIdHash(task),
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
            SampleTotal = ParseSampleTotal(task.SampleText),
            SavedPath = task.SavedPath,
            LastUpdateTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 将RenderTaskViewModel转换为CurrentTaskProgress
    /// </summary>
    public static CurrentTaskProgress ToCurrentTaskProgress(this RenderTaskViewModel task)
    {
        return new CurrentTaskProgress
        {
            TaskId = GetTaskIdHash(task),
            FileName = Path.GetFileName(task.BlendFilePath),
            CurrentFrame = task.CurrentFrame,
            OverallProgress = task.OverallProgress01,
            CurrentFrameProgress = task.Progress01,
            Status = task.Status,
            RealtimeProgress = new RealtimeRenderProgress
            {
                CurrentFrame = task.CurrentFrame,
                SampleCurrent = ParseSampleCurrent(task.SampleText),
                MemoryMB = null, // 需要从其他地方获取
                Elapsed = null, // 需要从其他地方获取
                SavedPath = task.SavedPath
            }
        };
    }

    /// <summary>
    /// 将RenderTaskViewModel转换为TaskStatusChange
    /// </summary>
    public static TaskStatusChange ToTaskStatusChange(this RenderTaskViewModel task)
    {
        return new TaskStatusChange
        {
            TaskId = GetTaskIdHash(task),
            FileName = Path.GetFileName(task.BlendFilePath),
            Status = task.Status,
            OverallProgress = task.OverallProgress01
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


    private static int? ParseSampleTotal(string sampleText)
    {
        if (string.IsNullOrEmpty(sampleText)) return null;

        var parts = sampleText.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[1], out var total))
        {
            return total;
        }

        return null;
    }

    private static int? ParseSampleCurrent(string sampleText)
    {
        if (string.IsNullOrEmpty(sampleText)) return null;

        var parts = sampleText.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out var current))
        {
            return current;
        }

        return null;
    }

    private static int GetTaskIdHash(RenderTaskViewModel task) => task.Id.GetHashCode();

    /// <summary>
    /// 将RenderTaskInfo转换为RenderTaskViewModel
    /// </summary>
    public static RenderTaskViewModel ToRenderTaskViewModel(this RenderTaskInfo taskInfo)
    {
        var task = new RenderTaskViewModel
        {
            Id = taskInfo.Id,
            BlendFilePath = taskInfo.Filepath,
            StartFrame = taskInfo.StartFrame,
            EndFrame = taskInfo.EndFrame,
            Enable = taskInfo.Enable
        };

        // 处理覆写数据
        if (taskInfo.Override != null)
        {
            if (taskInfo.Override.OverrideFrameRange != null)
            {
                task.OverrideFrameRange = true;
                task.StartFrame = taskInfo.Override.OverrideFrameRange.StartFrame;
                task.EndFrame = taskInfo.Override.OverrideFrameRange.EndFrame;
            }

            if (taskInfo.Override.OverrideScene != null)
            {
                task.OverrideScene = true;
                task.SelectedSceneName = taskInfo.Override.OverrideScene.SceneName;
            }
        }

        task.IsValid = !string.IsNullOrEmpty(task.BlendFilePath) && File.Exists(task.BlendFilePath);

        Console.WriteLine(
            $"[TaskInfoExtensions] Created RenderTaskViewModel from RenderTaskInfo - ID: {task.Id}, File: {Path.GetFileName(task.BlendFilePath)}, IsValid: {task.IsValid}");

        return task;
    }

    /// <summary>
    /// 将RenderTaskViewModel转换为RenderTaskInfo用于保存
    /// </summary>
    public static RenderTaskInfo ToRenderTaskInfo(this RenderTaskViewModel task)
    {
        var taskInfo = new RenderTaskInfo
        {
            Id = task.Id, // 使用现有的 UUID
            Filename = Path.GetFileName(task.BlendFilePath),
            Filepath = task.BlendFilePath,
            StartFrame = task.StartFrame,
            EndFrame = task.EndFrame,
            Enable = task.Enable
        };

        // If there are override settings, create override data
        if (task is { OverrideFrameRange: false, OverrideScene: false }) return taskInfo;

        taskInfo.Override = new OverrideData();
        if (task.OverrideFrameRange)
        {
            taskInfo.Override.OverrideFrameRange = new OverrideFrameRangeData
            {
                StartFrame = task.StartFrame,
                EndFrame = task.EndFrame
            };
        }

        if (task.OverrideScene && !string.IsNullOrEmpty(task.SelectedSceneName))
        {
            taskInfo.Override.OverrideScene = new OverrideSceneData
            {
                SceneName = task.SelectedSceneName
            };
        }

        return taskInfo;
    }
}