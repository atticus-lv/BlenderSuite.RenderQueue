using System.Collections.Generic;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Core;
using BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Error;
using BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Info;

namespace BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Business;

/// <summary>
/// 业务解析管道工厂
/// </summary>
public static class BusinessParsePipelineFactory
{
    /// <summary>
    /// 创建渲染业务解析管道
    /// </summary>
    public static BusinessParsePipeline<RenderEvent> CreateRenderPipeline()
    {
        // 1. 创建基础解析管道
        var errorClassifier = new BlenderErrorClassifier();
        var renderProgressParser = new RenderProgressParser();
        var basePipeline = new ParsePipeline(errorClassifier, renderProgressParser);

        // 2. 创建业务解析器
        var businessParser = new RenderBusinessParser();

        // 3. 组合成业务解析管道
        return new BusinessParsePipeline<RenderEvent>(basePipeline, businessParser);
    }

    /// <summary>
    /// 创建视频业务解析管道
    /// </summary>
    public static BusinessParsePipeline<object> CreateVideoPipeline()
    {
        // 1. 创建基础解析管道
        var errorClassifier = new BlenderErrorClassifier();
        var videoProgressParser = new VideoProgressParser();
        var basePipeline = new ParsePipeline(errorClassifier, videoProgressParser);

        // 2. 创建业务解析器（视频业务解析器待实现）
        var businessParser = new VideoBusinessParser();

        // 3. 组合成业务解析管道
        return new BusinessParsePipeline<object>(basePipeline, businessParser);
    }

    /// <summary>
    /// 创建查询业务解析管道
    /// </summary>
    public static BusinessParsePipeline<object> CreateQueryPipeline()
    {
        // 1. 创建基础解析管道
        var errorClassifier = new BlenderErrorClassifier();
        var queryResultParser = new QueryResultParser();
        var basePipeline = new ParsePipeline(errorClassifier, queryResultParser);

        // 2. 创建业务解析器（查询业务解析器待实现）
        var businessParser = new QueryBusinessParser();

        // 3. 组合成业务解析管道
        return new BusinessParsePipeline<object>(basePipeline, businessParser);
    }
}

// 占位符类，待实现
public class VideoBusinessParser : IBusinessParser<object>
{
    public List<object> ParseBusinessEvents(string line) => new();
    public object? GetCurrentState() => null;
    public void Reset() { }
}

public class QueryBusinessParser : IBusinessParser<object>
{
    public List<object> ParseBusinessEvents(string line) => new();
    public object? GetCurrentState() => null;
    public void Reset() { }
}
