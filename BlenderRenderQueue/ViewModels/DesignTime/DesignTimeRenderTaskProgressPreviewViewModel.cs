using System;
using System.Collections.Generic;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.ViewModels.DesignTime;

public class DesignTimeRenderTaskProgressPreviewViewModel : RenderTaskViewModel
{
    public DesignTimeRenderTaskProgressPreviewViewModel()
    {
        // 设置基本文件信息
        BlendFilePath = @"C:\Users\Design\Documents\MyProject\sample_scene.blend";
        // BlendFileName 是只读属性，会自动从 BlendFilePath 计算
        
        // 设置文件信息
        FileInfo = new BlendFileInfo
        {
            FilePath = BlendFilePath,
            FileSizeBytes = 15728640, // 15MB
            CreatedTime = DateTime.Now.AddDays(-7),
            LastModifiedTime = DateTime.Now.AddHours(-2),
            Thumbnail = null // 设计时不需要缩略图
        };
        
        // 设置场景属性 - 通过设置ViewModel的基础属性来影响计算属性
        // 这些属性会被ScenePropertiesView的计算属性使用
        // 注意：ScenePropertiesView会自动从这些属性计算得出
        
        // 设置渲染状态
        Status = RenderTaskStatus.Running;
        
        // 设置进度信息
        CompletedFrames = 45;
        CurrentFrame = 46;
        SampleText = "128/256";
        
        // 设置覆写信息
        OverrideFrameRange = false;
        OverrideScene = true;
        SelectedSceneName = "Scene.001";
        
        // 模拟渲染图片路径
        RenderedImagePath = @"C:\Users\Design\Documents\MyProject\render\image0045.jpg";
        HasRenderedImage = true;
        
        // 设置任务启用状态
        Enable = true;
        IsValid = true;
        
        // 设置渲染设置
        Animation = true;
        StartFrame = 1;
        EndFrame = 100;
        // 设置全局超时（设计时使用默认值）
        SetGlobalRenderTimeout(1800);
    }
}
