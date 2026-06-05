using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.Business.Blender.BlenderProcess;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

public sealed class BlenderQueryService : IBlenderQueryService
{
    private const string Prefix = "[BRQ] ";
    private readonly IRenderLogService? _logService;

    public BlenderQueryService(IRenderLogService? logService = null)
    {
        _logService = logService;
    }

    /// <summary>
    /// 使用新的进程管理服务查询文件属性
    /// </summary>
    public async Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesWithTempProcessAsync(
        string blenderPath,
        string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        using var processService = new BlenderProcessService(blenderPath, _logService);
        
        return await processService.ExecuteQueryAsync(
            GetFilePropertiesScript(blendFilePath),
            "get_all_file_properties",
            result => ParseFilePropertiesResult(result, blendFilePath),
            cancellationToken);
    }

    /// <summary>
    /// 获取文件属性查询脚本
    /// </summary>
    private string GetFilePropertiesScript(string blendFilePath)
    {
        var normalizedPath = EscapePathForPython(blendFilePath);
        var cmd = "get_all_file_properties";
        
        return GenerateFilePropertiesScript(normalizedPath, cmd);
    }

    /// <summary>
    /// 生成文件属性查询的 Python 脚本
    /// </summary>
    private static string GenerateFilePropertiesScript(string normalizedPath, string cmd)
    {
        return @"
import bpy, json
import os

def safe_get(func, default=None):
    try:
        return func()
    except:
        return default

def get_referenced_scenes(scene):
    try:
        # 检查是否有sequence_editor
        if not hasattr(scene, 'sequence_editor') or scene.sequence_editor is None:
            return []
        
        # 检查是否有strips_all
        if not hasattr(scene.sequence_editor, 'strips_all'):
            return []
        
        # 如果strips_all为空，跳过
        if len(scene.sequence_editor.strips_all) == 0:
            return []
        
        # 使用列表推导式获取所有SCENE类型的strip
        strip_scenes = [strip.scene.name for strip in scene.sequence_editor.strips_all if strip.type == 'SCENE']
        
        # 返回去重后的列表
        return list(set(strip_scenes))
    except Exception:
        return []

def get_timeline_cameras(scene):
    try:
        # 检查是否有timeline_markers
        if not hasattr(scene, 'timeline_markers'):
            return []
        
        # 获取所有时间轴标记中的相机
        cams = [m.camera for m in scene.timeline_markers if m.camera and m.camera.type == 'CAMERA']
        
        # 返回去重后的相机名称列表
        return list(set([cam.name for cam in cams if cam.name]))
    except Exception:
        return []

filepath = '" + normalizedPath + @"'
try:
    bpy.ops.wm.open_mainfile(filepath=filepath)
    
    scene_data = {}
    active_scene_name = bpy.context.scene.name
    
    for scene in bpy.data.scenes:
        scene_data[scene.name] = {
            'frame_start': safe_get(lambda: int(scene.frame_start), 1),
            'frame_end': safe_get(lambda: int(scene.frame_end), 1),
            'frame_current': safe_get(lambda: int(scene.frame_current), 1),
            'camera': safe_get(lambda: scene.camera.name if scene.camera else None),
            'render_output_path': safe_get(lambda: scene.render.filepath, ''),
            'render_output_format': safe_get(lambda: scene.render.image_settings.file_format, 'PNG'),
            'render_engine': safe_get(lambda: scene.render.engine, 'BLENDER_EEVEE'),
            'fps': safe_get(lambda: scene.render.fps, 24.0),
            'frame_path': safe_get(lambda: scene.render.frame_path() if hasattr(scene.render, 'frame_path') else None),
            'cycles_time_limit': safe_get(lambda: scene.cycles.time_limit if hasattr(scene, 'cycles') else None),
            'referenced_scenes': safe_get(lambda: get_referenced_scenes(scene), []),
            'timeline_cameras': safe_get(lambda: get_timeline_cameras(scene), [])
        }
    
    data = {
        'active_scene': active_scene_name,
        'scene_data': scene_data
    }
    
    print('" + Prefix + @"'+json.dumps({'cmd':'" + cmd + @"','ok':True,'data': data}, separators=(',', ':')))
except Exception as e:
    print('" + Prefix + @"'+json.dumps({'cmd':'" + cmd + @"','ok':False,'err':str(e)}, separators=(',', ':')))
";
    }

    /// <summary>
    /// 解析文件属性查询结果
    /// </summary>
    private (string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData) ParseFilePropertiesResult(string result, string blendFilePath)
    {
        _logService?.Write(RenderLogLevel.Debug, RenderLogScope.Worker, $"Raw result received: {result}", source: "BlenderQueryService");
        
        // 查找包含 [BRQ] 前缀的行
        var lines = result.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string? jsonLine = null;
        
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.StartsWith(Prefix))
            {
                jsonLine = line.Substring(Prefix.Length);
                break;
            }
        }
        
        if (string.IsNullOrEmpty(jsonLine))
        {
            _logService?.Write(RenderLogLevel.Warning, RenderLogScope.Worker, $"No JSON result found in output", source: "BlenderQueryService");
            throw new InvalidOperationException("未找到有效的JSON结果");
        }
        
        _logService?.Write(RenderLogLevel.Debug, RenderLogScope.Worker, $"JSON line: {jsonLine}", source: "BlenderQueryService");
        
        var root = JsonDocument.Parse(jsonLine).RootElement;
        
        if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var error = root.TryGetProperty("err", out var errProp) ? errProp.GetString() : "未知错误";
            _logService?.Write(RenderLogLevel.Error, RenderLogScope.Worker, $"Query failed: {error}", source: "BlenderQueryService");
            throw new InvalidOperationException($"查询失败: {error}");
        }
        
        var data = root.GetProperty("data");
        var activeScene = data.GetProperty("active_scene").GetString() ?? string.Empty;
        var sceneData = new Dictionary<string, BlendSceneProperties>();
        
        var scenesData = data.GetProperty("scene_data");
        foreach (var sceneProperty in scenesData.EnumerateObject())
        {
            var sceneName = sceneProperty.Name;
            var sceneInfo = sceneProperty.Value;
            
            sceneData[sceneName] = new BlendSceneProperties
            {   
                IsDefaultScene = activeScene == sceneName,
                FilePath = blendFilePath,
                FrameStart = sceneInfo.GetProperty("frame_start").GetInt32(),
                FrameEnd = sceneInfo.GetProperty("frame_end").GetInt32(),
                FrameCurrent = sceneInfo.GetProperty("frame_current").GetInt32(),
                CameraName = sceneInfo.GetProperty("camera").ValueKind == JsonValueKind.Null
                    ? null
                    : sceneInfo.GetProperty("camera").GetString(),
                RenderOutputPath = sceneInfo.GetProperty("render_output_path").GetString(),
                RenderOutputFormat = sceneInfo.GetProperty("render_output_format").GetString(),
                RenderEngine = sceneInfo.GetProperty("render_engine").GetString(),
                SceneName = sceneName,
                Fps = sceneInfo.GetProperty("fps").ValueKind == JsonValueKind.Null
                    ? null
                    : sceneInfo.GetProperty("fps").GetDouble(),
                FramePath = sceneInfo.GetProperty("frame_path").ValueKind == JsonValueKind.Null
                    ? null
                    : sceneInfo.GetProperty("frame_path").GetString(),
                CyclesTimeLimit = sceneInfo.GetProperty("cycles_time_limit").ValueKind == JsonValueKind.Null
                    ? null
                    : sceneInfo.GetProperty("cycles_time_limit").GetDouble(),
                ReferencedScenes = sceneInfo.GetProperty("referenced_scenes").ValueKind == JsonValueKind.Null
                    ? null
                    : sceneInfo.GetProperty("referenced_scenes").EnumerateArray().Select(x => x.GetString()!).Where(_ => true).ToList(),
                TimelineCameras = sceneInfo.GetProperty("timeline_cameras").ValueKind == JsonValueKind.Null
                    ? null
                    : sceneInfo.GetProperty("timeline_cameras").EnumerateArray().Select(x => x.GetString()!).Where(_ => true).ToList()
            };
        }
        
        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Worker, $"Successfully parsed {sceneData.Count} scenes", source: "BlenderQueryService");
        return (activeScene, sceneData);
    }


    /// <summary>
    /// 对文件路径进行简单的标准化处理
    /// </summary>
    private static string EscapePathForPython(string path)
    {
        // 只做最基本的反斜杠转换，Blender支持中文路径
        return path.Replace("\\", "/");
    }
}
