using System.Collections.Generic;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.ViewModels.DesignTime;

/// <summary>
/// 设计时用的 BlendScenePropertiesViewModel
/// </summary>
public class DesignTimeBlendScenePropertiesViewModel : BlendScenePropertiesViewModel
{
    public DesignTimeBlendScenePropertiesViewModel()
    {
        // 模拟场景数据
        var scene1 = new BlendSceneProperties
        {
            FilePath = @"C:\Users\Design\Documents\Blender\MyAnimation.blend",
            SceneName = "DefaultScene",
            IsDefaultScene = true,
            FrameStart = 1,
            FrameEnd = 250,
            FrameCurrent = 125,
            Fps = 24.0,
            RenderEngine = "CYCLES",
            CyclesTimeLimit = 300.0,
            CameraName = "Camera",
            RenderOutputPath = "/render/output/path",
            FramePath = "/render/output/path/frame_####.png",
            RenderOutputFormat = "PNG",
            ReferencedScenes = null, // 单一场景
            TimelineCameras = null   // 单一相机
        };

        var scene2 = new BlendSceneProperties
        {
            FilePath = @"C:\Users\Design\Documents\Blender\Animation2.blend",
            SceneName = "Animation",
            FrameStart = 1,
            FrameEnd = 100,
            FrameCurrent = 50,
            Fps = 30.0,
            RenderEngine = "BLENDER_EEVEE",
            CyclesTimeLimit = null,
            CameraName = "MainCamera",
            RenderOutputPath = "/render/eevee/output",
            FramePath = "/render/eevee/output/frame_####.jpg",
            RenderOutputFormat = "JPEG",
            ReferencedScenes = null, // 单一场景
            TimelineCameras = null   // 单一相机
        };

        // 设置所有场景数据
        AllScenes = new Dictionary<string, BlendSceneProperties>
        {
            { "Scene", scene1 },
            { "Animation", scene2 },
            { "Render_Scene", new BlendSceneProperties
            {
                FilePath = @"C:\Users\Design\Documents\Blender\RenderScene.blend",
                SceneName = "Render_Scene",
                FrameStart = 1,
                FrameEnd = 500,
                FrameCurrent = 250,
                Fps = 24.0,
                RenderEngine = "CYCLES",
                CyclesTimeLimit = 600.0,
                CameraName = "RenderCamera",
                RenderOutputPath = "/final/render/output",
                FramePath = "/final/render/output/frame_####.exr",
                RenderOutputFormat = "OPEN_EXR",
                ReferencedScenes = new List<string> { "Scene1", "Scene2" }, // 复合场景
                TimelineCameras = new List<string> { "Camera1", "Camera2", "Camera3" } // 多相机场景
            }}
        };

        // 设置当前选择的场景
        SelectedScene = scene1;
        
        // 设置场景属性
        SceneProperties = scene1;
        
        // 设置加载状态
        IsLoading = false;
        LoadingMessage = "加载完成";
        ErrorMessage = string.Empty;
    }
}
