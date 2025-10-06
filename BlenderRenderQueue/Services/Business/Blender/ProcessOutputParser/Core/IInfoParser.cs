using System.Collections.Generic;

namespace BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

/// <summary>
/// 信息解析器接口
/// </summary>
/// <typeparam name="T">解析的信息类型</typeparam>
public interface IInfoParser<T>
{
    /// <summary>
    /// 解析信息类型
    /// </summary>
    InfoType? TryParseInfoType(string line);
    
    /// <summary>
    /// 解析具体信息
    /// </summary>
    T? ParseInfo(string line);
    
    /// <summary>
    /// 生成事件
    /// </summary>
    List<object> GenerateEvents(T info);
}
