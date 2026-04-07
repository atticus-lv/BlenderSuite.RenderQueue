using System.Collections.Generic;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Models;

namespace BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

/// <summary>
/// 输出解析结果
/// </summary>
public class OutputParseResult
{
    public ErrorLevel ErrorLevel { get; set; } = ErrorLevel.Ignore;
    public InfoType? InfoType { get; set; }
    public string OriginalLine { get; set; } = string.Empty;
    public string ProcessedLine { get; set; } = string.Empty;
    
    // 特定类型的数据
    public RenderProgress? RenderProgress { get; set; }
    public VideoProgress? VideoProgress { get; set; }
    public QueryResult? QueryResult { get; set; }
    
    // 事件列表
    public List<object> Events { get; set; } = new();
    
    public bool HasError => ErrorLevel >= ErrorLevel.Error;
    public bool ShouldIgnore => ErrorLevel == ErrorLevel.Ignore;
}
