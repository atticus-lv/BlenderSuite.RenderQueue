using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Services.Business.Blender.BlenderProcess;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

public sealed class BlenderCommandService : IBlenderCommandService
{
    public async Task StartRenderAsync(IBlenderProcess process,
        string blendFilePath,
        bool animation,
        int? startFrame = null,
        int? endFrame = null,
        string? sceneName = null,
        CancellationToken cancellationToken = default)
    {
        // 注意：这里通过 Python 控制台环境执行，设置场景帧并调用渲染
        // 你可根据项目需要扩展渲染设置（引擎、样本数、输出路径等）
        var filePathLiteral = PythonScriptLiteral.FromString(blendFilePath);
        var sb = new StringBuilder();
        sb.AppendLine("import bpy");
        sb.AppendLine($"filepath = {filePathLiteral}");
        sb.AppendLine("bpy.ops.wm.open_mainfile(filepath=filepath)");

        // 构建渲染命令
        var renderCommand = $"bpy.ops.render.render(animation={(animation ? "True" : "False")}";

        // 添加帧范围参数
        if (startFrame.HasValue && endFrame.HasValue)
        {
            renderCommand += $", frame_start={startFrame.Value}, frame_end={endFrame.Value}";
        }

        renderCommand += ")";

        // 如果指定了场景名称，使用场景覆写
        if (!string.IsNullOrEmpty(sceneName))
        {
            var sceneNameLiteral = PythonScriptLiteral.FromString(sceneName);
            sb.AppendLine($"with bpy.context.temp_override(scene=bpy.data.scenes[{sceneNameLiteral}]):");
            sb.AppendLine($"    {renderCommand}");
        }
        else
        {
            sb.AppendLine(renderCommand);
        }

        await process.ExecuteScriptAsync(sb.ToString(), cancellationToken);
    }
}
