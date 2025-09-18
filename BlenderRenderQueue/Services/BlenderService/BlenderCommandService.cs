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
		var normalizedPath = EscapePathForPython(blendFilePath);
		var sb = new StringBuilder();
		sb.AppendLine("import bpy");
		sb.AppendLine($"filepath = '{normalizedPath}'");
		sb.AppendLine("bpy.ops.wm.open_mainfile(filepath=filepath)");
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

	/// <summary>
	/// 对文件路径进行简单的标准化处理
	/// </summary>
	private static string EscapePathForPython(string path)
	{
		// 只做最基本的反斜杠转换，Blender支持中文路径
		return path.Replace("\\", "/");
	}
} 