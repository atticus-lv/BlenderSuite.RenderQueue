using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.ViewModels.DesignTime;

/// <summary>
/// 设计时用的 RenderQueueViewModel
/// </summary>
public class DesignTimeRenderQueueViewModel : RenderQueueViewModel
{
    public DesignTimeRenderQueueViewModel()
    {
        // 创建多个设计时任务
        var task1 = new DesignTimeRenderTaskViewModel();
        task1.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation1.blend";
        task1.Status = RenderTaskStatus.Running;
        task1.Progress01 = 0.75;
        task1.OverallProgress01 = 0.60;
        task1.CurrentFrame = 180;
        task1.CompletedFrames = 150;
        task1.OverrideScene = true;
        task1.SelectedSceneName = "Animation";
        
        var task2 = new DesignTimeRenderTaskViewModel();
        task2.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation2.blend";
        task2.Status = RenderTaskStatus.Pending;
        task2.Progress01 = 0.0;
        task2.OverallProgress01 = 0.0;
        task2.CurrentFrame = 0;
        task2.CompletedFrames = 0;
        task2.OverrideScene = false;
        task2.SelectedSceneName = "Scene";
        
        var task3 = new DesignTimeRenderTaskViewModel();
        task3.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation3.blend";
        task3.Status = RenderTaskStatus.Completed;
        task3.Progress01 = 1.0;
        task3.OverallProgress01 = 1.0;
        task3.CurrentFrame = 250;
        task3.CompletedFrames = 250;
        task3.OverrideScene = true;
        task3.SelectedSceneName = "Render_Scene";
        
        var task4 = new DesignTimeRenderTaskViewModel();
        task4.BlendFilePath = @"C:\Users\Design\Documents\Blender\LoadingFile.blend";
        task4.Status = RenderTaskStatus.Pending;
        task4.IsValid = true;
        task4.OverrideScene = false;
        // 设置加载状态
        task4.ScenePropertiesView.IsLoading = true;
        
        var task5 = new DesignTimeRenderTaskViewModel();
        task5.BlendFilePath = @"C:\Users\Design\Documents\Blender\Animation5.blend";
        task5.Status = RenderTaskStatus.Failed;
        task5.Progress01 = 0.0;
        task5.OverallProgress01 = 0.0;
        task5.OverrideScene = true;
        task5.SelectedSceneName = "Animation";
        
        // 设置任务集合
        RenderTasks = new ObservableCollection<RenderTaskViewModel>
        {
            task1, task2, task3, task4, task5
        };
        
        // 设置选中任务
        SelectedTask = task1;
        
        // 设置当前渲染任务
        CurrentRenderingTask = task1;
        
        // 设置队列状态
        QueueState = QueueState.Running;
        ActiveTaskCount = 1;
        CompletedTaskCount = 1;
        FailedTaskCount = 1;
        QueueStatusText = "运行中 (1 个任务)";
        
        // 设置其他属性
        AutoStartNext = true;
        IsGeneratingVideo = false;
        VideoGenerationProgress = 0.0;
        VideoGenerationStatus = string.Empty;
        
        // 设置模拟的剩余时间
        RemainingTimeText = "00:05:30";
    }
}
