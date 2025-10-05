using BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Error;
using BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Info;

namespace BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Core;

/// <summary>
/// 解析管道工厂
/// </summary>
public static class ParsePipelineFactory
{
    /// <summary>
    /// 创建默认解析管道
    /// </summary>
    public static ParsePipeline CreateDefault()
    {
        var errorClassifier = new BlenderErrorClassifier();
        var renderParser = new RenderProgressParser();
        var videoParser = new VideoProgressParser();
        var queryParser = new QueryResultParser();
        
        return new ParsePipeline(
            errorClassifier,
            renderParser,
            videoParser,
            queryParser
        );
    }
    
    /// <summary>
    /// 创建渲染专用解析管道
    /// </summary>
    public static ParsePipeline CreateForRender()
    {
        var errorClassifier = new BlenderErrorClassifier();
        var renderParser = new RenderProgressParser();
        
        return new ParsePipeline(
            errorClassifier,
            renderParser
        );
    }
    
    /// <summary>
    /// 创建视频专用解析管道
    /// </summary>
    public static ParsePipeline CreateForVideo()
    {
        var errorClassifier = new BlenderErrorClassifier();
        var videoParser = new VideoProgressParser();
        
        return new ParsePipeline(
            errorClassifier,
            videoParser
        );
    }
    
    /// <summary>
    /// 创建查询专用解析管道
    /// </summary>
    public static ParsePipeline CreateForQuery()
    {
        var errorClassifier = new BlenderErrorClassifier();
        var queryParser = new QueryResultParser();
        
        return new ParsePipeline(
            errorClassifier,
            queryParser
        );
    }
}
