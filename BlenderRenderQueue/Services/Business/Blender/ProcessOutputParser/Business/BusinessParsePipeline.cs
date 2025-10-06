using System.Collections.Generic;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

namespace BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Business;

/// <summary>
/// 业务解析管道 - 组合基础解析器和业务解析器
/// </summary>
/// <typeparam name="TBusinessEvent">业务事件类型</typeparam>
public class BusinessParsePipeline<TBusinessEvent>
{
    private readonly ParsePipeline _basePipeline;
    private readonly IBusinessParser<TBusinessEvent> _businessParser;

    public BusinessParsePipeline(ParsePipeline basePipeline, IBusinessParser<TBusinessEvent> businessParser)
    {
        _basePipeline = basePipeline;
        _businessParser = businessParser;
    }

    /// <summary>
    /// 解析输出行，返回基础解析结果和业务事件
    /// </summary>
    public BusinessParseResult<TBusinessEvent> ParseLine(string line)
    {
        // 1. 使用基础管道进行解析
        var baseResult = _basePipeline.ParseLine(line);

        // 2. 使用业务解析器解析业务事件
        var businessEvents = _businessParser.ParseBusinessEvents(line);

        return new BusinessParseResult<TBusinessEvent>
        {
            BaseResult = baseResult,
            BusinessEvents = businessEvents,
            CurrentBusinessState = _businessParser.GetCurrentState()
        };
    }

    /// <summary>
    /// 获取当前业务状态
    /// </summary>
    public object? CurrentBusinessState => _businessParser.GetCurrentState();

    /// <summary>
    /// 重置业务解析器状态
    /// </summary>
    public void Reset()
    {
        _businessParser.Reset();
    }
}

/// <summary>
/// 业务解析结果
/// </summary>
/// <typeparam name="TBusinessEvent">业务事件类型</typeparam>
public class BusinessParseResult<TBusinessEvent>
{
    public OutputParseResult BaseResult { get; set; } = new();
    public List<TBusinessEvent> BusinessEvents { get; set; } = new();
    public object? CurrentBusinessState { get; set; }
}
