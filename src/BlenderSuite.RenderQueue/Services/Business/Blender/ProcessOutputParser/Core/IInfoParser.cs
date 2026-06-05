using System.Collections.Generic;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

public interface IInfoParser
{
    InfoType? TryParseInfoType(string line);
    object? ParseInfoObject(string line);
    IReadOnlyList<object> GenerateEventsObject(object info);
}

/// <summary>
/// 信息解析器接口
/// </summary>
/// <typeparam name="T">解析的信息类型</typeparam>
public interface IInfoParser<T> : IInfoParser
{
    /// <summary>
    /// 解析信息类型
    /// </summary>
    new InfoType? TryParseInfoType(string line);
    
    /// <summary>
    /// 解析具体信息
    /// </summary>
    T? ParseInfo(string line);
    
    /// <summary>
    /// 生成事件
    /// </summary>
    List<object> GenerateEvents(T info);

    object? IInfoParser.ParseInfoObject(string line) => ParseInfo(line);

    IReadOnlyList<object> IInfoParser.GenerateEventsObject(object info)
    {
        return info is T typedInfo ? GenerateEvents(typedInfo) : [];
    }
}
