using BlenderRenderQueue.Services.Business.BlenderService.ProcessOutputParser.Core;

namespace BlenderRenderQueue.Services.Business.BlenderService.BlenderProcess;

/// <summary>
/// Blender查询进程 - 用于查询文件属性、版本信息等
/// </summary>
public class BlenderQueryProcess : BaseBlenderProcess
{
    public override BlenderProcessType ProcessType => BlenderProcessType.Query;

    public BlenderQueryProcess(string blenderPath) 
        : base(blenderPath, BlenderProcessConfig.CreateQueryConfig(), ParsePipelineFactory.CreateForQuery())
    {
    }

    /// <summary>
    /// 查询进程特有的错误处理逻辑
    /// </summary>
    protected override void HandleErrorOutput(string errorData)
    {
        // 现在使用新的解析器架构，这个方法主要用于向后兼容
        // 实际的错误处理由 ProcessOutputLine 完成
        ProcessOutputLine(errorData);
    }
}
