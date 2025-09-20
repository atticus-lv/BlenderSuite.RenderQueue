using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public sealed class BlenderCommandService : IBlenderCommandService
{
	public async Task StartRenderAsync(BasePythonProcessService process,
		string blendFilePath,
		bool animation,
		int? startFrame = null,
		int? endFrame = null,
		CancellationToken cancellationToken = default)
	{
		// 注意：这里通过 Python 控制台环境执行，设置场景帧并调用渲染
		// 你可根据项目需要扩展渲染设置（引擎、样本数、输出路径等）
		var normalizedPath = EscapePathForPython(blendFilePath);
		var sb = new StringBuilder();
		sb.AppendLine("import bpy");
		sb.AppendLine($"filepath = '{normalizedPath}'");
		sb.AppendLine("bpy.ops.wm.open_mainfile(filepath=filepath)");
		
		// 构建渲染命令，只有当提供了帧范围参数时才使用自定义帧范围
		if (startFrame.HasValue && endFrame.HasValue)
		{
			sb.AppendLine($"bpy.ops.render.render(animation={(animation ? "True" : "False")}, start_frame={startFrame.Value}, end_frame={endFrame.Value})");
		}
		else
		{
			sb.AppendLine($"bpy.ops.render.render(animation={(animation ? "True" : "False")})");
		}

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