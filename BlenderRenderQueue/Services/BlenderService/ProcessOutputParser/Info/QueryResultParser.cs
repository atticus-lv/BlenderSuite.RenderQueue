using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Core;
using BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Models;

namespace BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Info;

/// <summary>
/// 查询结果解析器
/// </summary>
public class QueryResultParser : IInfoParser<QueryResult>
{
    private static readonly Regex JsonResultRegex = new(@"\[BRQ\]\s*(.+)", RegexOptions.Compiled);
    private static readonly Regex CommandRegex = new(@"""cmd"":\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex SuccessRegex = new(@"""ok"":\s*(true|false)", RegexOptions.Compiled);
    private static readonly Regex DataRegex = new(@"""data"":\s*(\{[^}]*\})", RegexOptions.Compiled);
    private static readonly Regex ErrorRegex = new(@"""err"":\s*""([^""]+)""", RegexOptions.Compiled);
    
    public InfoType? TryParseInfoType(string line)
    {
        if (JsonResultRegex.IsMatch(line))
        {
            return InfoType.QueryResult;
        }
        return null;
    }
    
    public QueryResult? ParseInfo(string line)
    {
        var jsonMatch = JsonResultRegex.Match(line);
        if (!jsonMatch.Success)
            return null;
            
        var jsonContent = jsonMatch.Groups[1].Value;
        var result = new QueryResult
        {
            RawOutput = line
        };
        
        try
        {
            // 尝试解析完整的JSON
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            
            // 解析命令
            if (root.TryGetProperty("cmd", out var cmdProp))
            {
                result.Command = cmdProp.GetString();
            }
            
            // 解析成功状态
            if (root.TryGetProperty("ok", out var okProp))
            {
                result.IsSuccess = okProp.GetBoolean();
            }
            
            // 解析数据
            if (root.TryGetProperty("data", out var dataProp))
            {
                result.Data = dataProp.GetRawText();
            }
            
            // 解析错误
            if (root.TryGetProperty("err", out var errProp))
            {
                result.Error = errProp.GetString();
            }
        }
        catch (JsonException)
        {
            // 如果JSON解析失败，回退到正则表达式解析
            var cmdMatch = CommandRegex.Match(jsonContent);
            if (cmdMatch.Success)
            {
                result.Command = cmdMatch.Groups[1].Value;
            }
            
            var successMatch = SuccessRegex.Match(jsonContent);
            if (successMatch.Success)
            {
                result.IsSuccess = successMatch.Groups[1].Value == "true";
            }
            
            var dataMatch = DataRegex.Match(jsonContent);
            if (dataMatch.Success)
            {
                result.Data = dataMatch.Groups[1].Value;
            }
            
            var errorMatch = ErrorRegex.Match(jsonContent);
            if (errorMatch.Success)
            {
                result.Error = errorMatch.Groups[1].Value;
            }
        }
        
        return result;
    }
    
    public List<object> GenerateEvents(QueryResult result)
    {
        var events = new List<object>();
        
        // 这里可以创建查询相关的事件
        // 例如：QueryCompletedEvent, QueryFailedEvent 等
        // 暂时返回空列表，后续可以根据需要添加
        
        return events;
    }
}
