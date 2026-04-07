using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

/// <summary>
/// 解析管道
/// </summary>
public class ParsePipeline
{
    private readonly IErrorClassifier _errorClassifier;
    private readonly List<IInfoParser> _infoParsers;
    
    public ParsePipeline(IErrorClassifier errorClassifier, params IInfoParser[] infoParsers)
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
        foreach (var parser in _infoParsers)
        {
            var infoType = parser.TryParseInfoType(line);
            if (infoType.HasValue)
            {
                result.InfoType = infoType.Value;
                
                // 解析具体信息
                var info = parser.ParseInfoObject(line);
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

                    result.Events.AddRange(parser.GenerateEventsObject(info));
                }

                break;
            }
        }
        
        return result;
    }
}
