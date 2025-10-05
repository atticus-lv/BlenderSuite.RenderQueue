using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.BlenderService.BlenderProcess;

namespace BlenderRenderQueue.Services.Business.BlenderService;

public sealed class BlenderQueryService : IBlenderQueryService
{
    private const string Prefix = "[BRQ] ";

    /// <summary>
    /// 使用新的进程管理服务查询文件属性
    /// </summary>
    public async Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesWithTempProcessAsync(
        string blenderPath,
        string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        using var processService = new BlenderProcessService(blenderPath);
        
        return await processService.ExecuteQueryAsync(
            GetFilePropertiesScript(blendFilePath),
            "get_all_file_properties",
            result =>
            {
                Console.WriteLine($"[BlenderQueryService] Raw result received: {result}");
                
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
                    Console.WriteLine($"[BlenderQueryService] No JSON result found in output");
                    throw new InvalidOperationException("未找到有效的JSON结果");
                }
                
                Console.WriteLine($"[BlenderQueryService] JSON line: {jsonLine}");
                
                var root = JsonDocument.Parse(jsonLine).RootElement;
                
                if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
                {
                    var error = root.TryGetProperty("err", out var errProp) ? errProp.GetString() : "未知错误";
                    Console.WriteLine($"[BlenderQueryService] Query failed: {error}");
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
                
                Console.WriteLine($"[BlenderQueryService] Successfully parsed {sceneData.Count} scenes");
                return (activeScene, sceneData);
            },
            cancellationToken);
    }

    /// <summary>
    /// 获取文件属性查询脚本
    /// </summary>
    private string GetFilePropertiesScript(string blendFilePath)
    {
        var normalizedPath = blendFilePath.Replace("\\", "/");
        var cmd = "get_all_file_properties";
        
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
        return [ref.name for ref in scene.sequence_editor.sequences if hasattr(ref, 'scene') and ref.scene]
    except:
        return []

def get_timeline_cameras(scene):
    try:
        return [seq.name for seq in scene.sequence_editor.sequences if seq.type == 'CAMERA']
    except:
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

    public async Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesAsync(IBlenderProcess process,
        string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync<(string, Dictionary<string, BlendSceneProperties>)>(
            process,
            blendFilePath,
            "get_all_file_properties",
            null, // 不再需要 dataPythonDictLiteral，在脚本中直接构建
            root =>
            {
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
                
                return (activeScene, sceneData);
            },
            cancellationToken);
    }

    private async Task<T> QueryAsync<T>(
        IBlenderProcess process,
        string blendFilePath,
        string cmd,
        string? dataPythonDictLiteral,
        Func<JsonElement, T> onOk,
        CancellationToken cancellationToken)
    {
        // 对路径进行简单的标准化处理
        var normalizedPath = EscapePathForPython(blendFilePath);
        string script;
        if (dataPythonDictLiteral == null)
        {
            // 使用内置的安全获取方式
            script = @"
import bpy, json
filepath = '" + normalizedPath + @"'
try:
    bpy.ops.wm.open_mainfile(filepath=filepath)
    
    # 安全地获取数据，对每个可能出错的操作使用 try-except
    def safe_get(operation, default=None):
        try:
            return operation()
        except Exception:
            return default
    
    # 获取场景引用的其他场景列表
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
    
    # 获取时间轴标记中的相机列表
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
    
    # 获取所有场景数据
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
        else
        {
            // 使用传统方式
            script = @"
import bpy, json
filepath = '" + normalizedPath + @"'
try:
    bpy.ops.wm.open_mainfile(filepath=filepath)
    s=bpy.context.scene
    print('" + Prefix + @"'+json.dumps({'cmd':'" + cmd + @"','ok':True,'data': " + dataPythonDictLiteral + @"}, separators=(',', ':')))
except Exception as e:
    print('" + Prefix + @"'+json.dumps({'cmd':'" + cmd + @"','ok':False,'err':str(e)}, separators=(',', ':')))
";
        }

        var output = await process.ExecuteScriptAsync(script, cancellationToken);
        return ParseResult(output, cmd, onOk);
    }

    private static T ParseResult<T>(string output, string cmd, Func<JsonElement, T> onOk)
    {
        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith(Prefix)) continue;
            using var doc = JsonDocument.Parse(line.Substring(Prefix.Length));
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
            {
                return onOk(root);
            }

            var err = root.TryGetProperty("err", out var eProp) ? eProp.GetString() : "unknown error";
            throw new InvalidOperationException($"{cmd} failed: {err}");
        }

        throw new InvalidOperationException($"No [BRQ] result found for {cmd}");
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