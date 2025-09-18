using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public sealed class BlenderCommandService : IBlenderCommandService
{
	public async Task StartRenderAsync(BasePythonProcessService process,
		string blendFilePath,
		int startFrame,
		int endFrame,
		bool animation,
		CancellationToken cancellationToken = default)
	{
		// 注意：这里通过 Python 控制台环境执行，设置场景帧并调用渲染
		// 你可根据项目需要扩展渲染设置（引擎、样本数、输出路径等）
		var escapedPath = blendFilePath.Replace("\\", "/");
		var sb = new StringBuilder();
		sb.AppendLine("import bpy");
		sb.AppendLine($"bpy.ops.wm.open_mainfile(filepath=r'{escapedPath}')");
		if (animation)
		{
			sb.AppendLine($"bpy.context.scene.frame_start = {startFrame}");
			sb.AppendLine($"bpy.context.scene.frame_end = {endFrame}");
		}
		else
		{
			sb.AppendLine($"bpy.context.scene.frame_set({startFrame})");
		}
		sb.AppendLine($"bpy.ops.render.render(animation={(animation ? "True" : "False")}, start_frame={startFrame}, end_frame={endFrame})");

		await process.ExecuteScript(sb.ToString(), "render_start", cancellationToken);
	}
} 