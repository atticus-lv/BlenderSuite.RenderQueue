namespace BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser.Models;

/// <summary>
/// 查询结果信息
/// </summary>
public class QueryResult
{
    public string? Command { get; set; }
    public bool IsSuccess { get; set; }
    public string? Data { get; set; }
    public string? Error { get; set; }
    public string? RawOutput { get; set; }
}
