using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public sealed class BlenderQueryService : IBlenderQueryService
{
	private const string Prefix = "[BRQ] ";

	public async Task<(int frameStart, int frameEnd)> GetSceneFramesAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default)
	{
		return await QueryAsync<(int, int)>(
			process,
			blendFilePath,
			"get_scene_frames",
			"{'frame_start': int(s.frame_start), 'frame_end': int(s.frame_end)}",
			root =>
			{
				var data = root.GetProperty("data");
				return (data.GetProperty("frame_start").GetInt32(), data.GetProperty("frame_end").GetInt32());
			},
			cancellationToken);
	}

	public async Task<string?> GetSceneCameraAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default)
	{
		return await QueryAsync<string?>(
			process,
			blendFilePath,
			"get_scene_camera",
			"{'camera': (s.camera.name if s.camera else None)}",
			root =>
			{
				var data = root.GetProperty("data");
				return data.GetProperty("camera").ValueKind == JsonValueKind.Null ? null : data.GetProperty("camera").GetString();
			},
			cancellationToken);
	}

	public async Task<string?> GetRenderOutputPathAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default)
	{
		return await QueryAsync<string?>(
			process,
			blendFilePath,
			"get_render_output_path",
			"{'path': bpy.context.scene.render.filepath}",
			root => root.GetProperty("data").GetProperty("path").GetString(),
			cancellationToken);
	}

	public async Task<string?> GetRenderOutputFormatAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default)
	{
		return await QueryAsync<string?>(
			process,
			blendFilePath,
			"get_render_output_format",
			"{'format': bpy.context.scene.render.image_settings.file_format}",
			root => root.GetProperty("data").GetProperty("format").GetString(),
			cancellationToken);
	}

	private async Task<T> QueryAsync<T>(
		BasePythonProcessService process,
		string blendFilePath,
		string cmd,
		string dataPythonDictLiteral,
		Func<JsonElement, T> onOk,
		CancellationToken cancellationToken)
	{
		var escapedPath = blendFilePath.Replace("\\", "/");
		var script = $@"
import bpy, json
try:
    bpy.ops.wm.open_mainfile(filepath=r'{escapedPath}')
    s=bpy.context.scene
    print('{Prefix}'+json.dumps({{'cmd':'{cmd}','ok':True,'data':{dataPythonDictLiteral}}}, separators=(',', ':')))
except Exception as e:
    print('{Prefix}'+json.dumps({{'cmd':'{cmd}','ok':False,'err':str(e)}}, separators=(',', ':')))
";

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
} 