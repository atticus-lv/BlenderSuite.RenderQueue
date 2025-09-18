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
		var escapedPath = blendFilePath.Replace("\\", "/");
		var script = $@"
import bpy, json
try:
    bpy.ops.wm.open_mainfile(filepath=r'{escapedPath}')
    s=bpy.context.scene
    print('{Prefix}'+json.dumps({{'cmd':'get_scene_frames','ok':True,'data':{{'frame_start':int(s.frame_start),'frame_end':int(s.frame_end)}}}}, separators=(',',':')))
except Exception as e:
    print('{Prefix}'+json.dumps({{'cmd':'get_scene_frames','ok':False,'err':str(e)}}, separators=(',',':')))
";

		var res = await process.ExecuteScript(script, "get_scene_frames", cancellationToken);
		// 在返回的输出里找最后一条带前缀的行
		var lines = res.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = lines.Length - 1; i >= 0; i--)
		{
			var line = lines[i].Trim();
			if (!line.StartsWith(Prefix)) continue;
			var json = line.Substring(Prefix.Length);
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
			{
				var data = root.GetProperty("data");
				var start = data.GetProperty("frame_start").GetInt32();
				var end = data.GetProperty("frame_end").GetInt32();
				return (start, end);
			}
			else
			{
				var err = root.TryGetProperty("err", out var eProp) ? eProp.GetString() : "unknown error";
				throw new InvalidOperationException($"get_scene_frames failed: {err}");
			}
		}

		throw new InvalidOperationException("No [BRQ] result found in output");
	}
} 