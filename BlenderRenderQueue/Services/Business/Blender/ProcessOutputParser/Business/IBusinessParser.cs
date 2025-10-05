using System.Collections.Generic;

namespace BlenderRenderQueue.Services.Business.BlenderService.ProcessOutputParser.Business;

/// <summary>
/// 业务解析器接口 - 面向具体业务场景的解析器
/// </summary>
/// <typeparam name="TBusinessEvent">业务事件类型</typeparam>
public interface IBusinessParser<TBusinessEvent>
{
    /// <summary>
    /// 解析业务事件
    /// </summary>
    /// <param name="line">输出行</param>
    /// <returns>业务事件列表</returns>
    List<TBusinessEvent> ParseBusinessEvents(string line);
    
    /// <summary>
    /// 获取当前业务状态
    /// </summary>
    object? GetCurrentState();
    
    /// <summary>
    /// 重置解析器状态
    /// </summary>
    void Reset();
}
