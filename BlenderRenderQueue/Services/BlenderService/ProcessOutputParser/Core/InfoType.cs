namespace BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Core;

/// <summary>
/// 信息类型枚举
/// </summary>
public enum InfoType
{
    /// <summary>渲染进度信息</summary>
    RenderProgress,
    
    /// <summary>视频进度信息</summary>
    VideoProgress,
    
    /// <summary>查询结果信息</summary>
    QueryResult,
    
    /// <summary>一般信息</summary>
    General
}
