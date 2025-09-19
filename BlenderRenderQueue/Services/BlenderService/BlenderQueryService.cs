using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.BlenderService;

public sealed class BlenderQueryService : IBlenderQueryService
{
    private const string Prefix = "[BRQ] ";

    public async Task<BlendSceneProperties> GetAllFilePropertiesAsync(BasePythonProcessService process,
        string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlendSceneProperties>(
            process,
            blendFilePath,
            "get_all_file_properties",
            null, // 不再需要 dataPythonDictLiteral，在脚本中直接构建
            root =>
            {
                var data = root.GetProperty("data");
                return new BlendSceneProperties
                {
                    FilePath = blendFilePath,
                    FrameStart = data.GetProperty("frame_start").GetInt32(),
                    FrameEnd = data.GetProperty("frame_end").GetInt32(),
                    CameraName = data.GetProperty("camera").ValueKind == JsonValueKind.Null
                        ? null
                        : data.GetProperty("camera").GetString(),
                    RenderOutputPath = data.GetProperty("render_output_path").GetString(),
                    RenderOutputFormat = data.GetProperty("render_output_format").GetString(),
                    RenderEngine = data.GetProperty("render_engine").GetString(),
                    SceneName = data.GetProperty("scene_name").GetString(),
                    Fps = data.GetProperty("fps").ValueKind == JsonValueKind.Null
                        ? null
                        : data.GetProperty("fps").GetDouble(),
                    FramePath = data.GetProperty("frame_path").ValueKind == JsonValueKind.Null
                        ? null
                        : data.GetProperty("frame_path").GetString()
                };
            },
            cancellationToken);
    }

    private async Task<T> QueryAsync<T>(
        BasePythonProcessService process,
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
            script = $@"
import bpy, json
filepath = '{normalizedPath}'
try:
    bpy.ops.wm.open_mainfile(filepath=filepath)
    s=bpy.context.scene
    
    # 安全地获取数据，对每个可能出错的操作使用 try-except
    def safe_get(operation, default=None):
        try:
            return operation()
        except Exception:
            return default
    
    data = {{
        'frame_start': safe_get(lambda: int(s.frame_start), 1),
        'frame_end': safe_get(lambda: int(s.frame_end), 1),
        'camera': safe_get(lambda: s.camera.name if s.camera else None),
        'render_output_path': safe_get(lambda: bpy.context.scene.render.filepath, ''),
        'render_output_format': safe_get(lambda: bpy.context.scene.render.image_settings.file_format, 'PNG'),
        'render_engine': safe_get(lambda: bpy.context.scene.render.engine, 'BLENDER_EEVEE'),
        'scene_name': safe_get(lambda: bpy.context.scene.name, 'Scene'),
        'fps': safe_get(lambda: bpy.context.scene.render.fps, 24.0),
        'frame_path': safe_get(lambda: bpy.context.scene.render.frame_path() if hasattr(bpy.context.scene.render, 'frame_path') else None)
    }}
    
    print('{Prefix}'+json.dumps({{'cmd':'{cmd}','ok':True,'data': data}}, separators=(',', ':')))
except Exception as e:
    print('{Prefix}'+json.dumps({{'cmd':'{cmd}','ok':False,'err':str(e)}}, separators=(',', ':')))
";
        }
        else
        {
            // 使用传统方式
            script = $@"
import bpy, json
filepath = '{normalizedPath}'
try:
    bpy.ops.wm.open_mainfile(filepath=filepath)
    s=bpy.context.scene
    print('{Prefix}'+json.dumps({{'cmd':'{cmd}','ok':True,'data': {dataPythonDictLiteral}}}, separators=(',', ':')))
except Exception as e:
    print('{Prefix}'+json.dumps({{'cmd':'{cmd}','ok':False,'err':str(e)}}, separators=(',', ':')))
";
        }

        var res = await process.ExecuteScript(script, cmd, cancellationToken);
        return ParseResult<T>(res.Output, cmd, onOk);
    }

    private static T ParseResult<T>(string output, string cmd, Func<JsonElement, T> onOk)
    {
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith(Prefix)) continue;
            using var doc = JsonDocument.Parse(line.Substring(Prefix.Length));
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
            {
                return onOk(root);
            }
            else
            {
                var err = root.TryGetProperty("err", out var eProp) ? eProp.GetString() : "unknown error";
                throw new InvalidOperationException($"{cmd} failed: {err}");
            }
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