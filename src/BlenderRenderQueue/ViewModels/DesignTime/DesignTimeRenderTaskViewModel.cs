using System;
using System.Collections.Generic;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.ViewModels.DesignTime;

/// <summary>
/// 设计时用的 RenderTaskViewModel
/// </summary>
public class DesignTimeRenderTaskViewModel : RenderTaskViewModel
{
    public DesignTimeRenderTaskViewModel()
    {
        // 设置基本属性
        BlendFilePath = @"C:\Users\Design\Documents\Blender\MyAnimation.blend";
        StartFrame = 1;
        EndFrame = 250;
        Animation = true;
        OverrideFrameRange = false;
        OverrideScene = true;
        SelectedSceneName = "Animation";
        AutoStart = true;
        Enable = true;
        IsValid = true;
        
        // 设置渲染状态
        Status = RenderTaskStatus.Running;
        Progress01 = 0.65; // 当前帧进度 65%
        OverallProgress01 = 0.45; // 整体进度 45%
        Engine = "CYCLES";
        CurrentFrame = 125;
        CompletedFrames = 112;
        
        // 设置全局超时（设计时使用默认值）
        SetGlobalRenderTimeout(1800); // 30分钟
        
        // 设置场景属性视图
        ScenePropertiesView = new DesignTimeBlendScenePropertiesViewModel();
        
        // 设置加载状态（设计时测试）
        ScenePropertiesView.IsLoading = false; // 设置为 false 显示正常状态
        
        // 设置可用场景名称
        AvailableSceneNames = new List<string> { "Scene", "Animation", "Render_Scene" };
        
        // 设置文件信息
        FileInfo = new BlendFileInfo
        {
            FilePath = BlendFilePath,
            FileSizeBytes = 15728640, // 15MB
            CreatedTime = DateTime.Now.AddDays(-7),
            LastModifiedTime = DateTime.Now.AddHours(-2),
            Thumbnail = null // 设计时不需要缩略图
        };
        
        // 设置渲染图片（设计时不需要）
        RenderedImage = null;
        
        // 设置日志
        OutputLog = @"[INFO] 开始渲染任务...
[INFO] 使用场景: Animation
[INFO] 帧范围: 1-250
[INFO] 渲染引擎: Cycles
[INFO] 当前帧: 125/250
[INFO] 预计剩余时间: 15分钟
[INFO] 内存使用: 2.3GB
[INFO] GPU使用率: 85%";
        
        IsLogPaused = false;
        
        // 设置持续时间
        Duration = TimeSpan.FromMinutes(25);
    }
}
