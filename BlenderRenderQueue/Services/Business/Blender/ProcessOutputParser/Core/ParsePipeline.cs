using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Services.Business.BlenderService.ProcessOutputParser.Core;

/// <summary>
/// 解析管道
/// </summary>
public class ParsePipeline
{
    private readonly IErrorClassifier _errorClassifier;
    private readonly List<object> _infoParsers;
    
    public ParsePipeline(IErrorClassifier errorClassifier, params object[] infoParsers)
    {
        _errorClassifier = errorClassifier;
        _infoParsers = infoParsers.ToList();
    }
    
    public OutputParseResult ParseLine(string line)
    {
        var result = new OutputParseResult
        {
            OriginalLine = line,
            ProcessedLine = line
        };
        
        // 第一步：错误分类
        result.ErrorLevel = _errorClassifier.ClassifyError(line);
        
        // 如果是严重错误或错误，直接返回
        if (result.ErrorLevel >= ErrorLevel.Error)
        {
            result.ProcessedLine = _errorClassifier.FormatError(line, result.ErrorLevel);
            return result;
        }
        
        // 第二步：信息类型解析
        foreach (var parserObj in _infoParsers)
        {
            // 使用反射来调用泛型方法
            var parserType = parserObj.GetType();
            var tryParseMethod = parserType.GetMethod("TryParseInfoType");
            var parseMethod = parserType.GetMethod("ParseInfo");
            var generateEventsMethod = parserType.GetMethod("GenerateEvents");
            
            if (tryParseMethod != null && parseMethod != null && generateEventsMethod != null)
            {
                var infoType = (InfoType?)tryParseMethod.Invoke(parserObj, new object[] { line });
                if (infoType.HasValue)
                {
                    result.InfoType = infoType.Value;
                    
                    // 解析具体信息
                    var info = parseMethod.Invoke(parserObj, new object[] { line });
                    if (info != null)
                    {
                        // 根据信息类型设置相应的属性
                        switch (infoType.Value)
                        {
                            case InfoType.RenderProgress:
                                result.RenderProgress = info as BlenderRenderQueue.Models.RenderProgress;
                                break;
                            case InfoType.VideoProgress:
                                result.VideoProgress = info as Models.VideoProgress;
                                break;
                            case InfoType.QueryResult:
                                result.QueryResult = info as Models.QueryResult;
                                break;
                        }
                        
                        // 生成事件
                        var events = (List<object>)generateEventsMethod.Invoke(parserObj, new object[] { info });
                        result.Events.AddRange(events);
                    }
                    break;
                }
            }
        }
        
        return result;
    }
}
