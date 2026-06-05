using System;
using System.Collections.Generic;
using BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser.Error;

/// <summary>
/// Blender错误分类器
/// </summary>
public class BlenderErrorClassifier : IErrorClassifier
{
    private static readonly Dictionary<string, ErrorLevel> ErrorPatterns = new()
    {
        // 严重错误 - 进程崩溃
        ["Blender quit"] = ErrorLevel.Critical,
        ["EXCEPTION_ACCESS_VIOLATION"] = ErrorLevel.Critical,
        ["Segmentation fault"] = ErrorLevel.Critical,
        ["Fatal error"] = ErrorLevel.Critical,
        
        // 错误 - 操作失败
        ["Cannot render, no camera"] = ErrorLevel.Error,
        ["File not found"] = ErrorLevel.Error,
        ["Permission denied"] = ErrorLevel.Error,
        ["Invalid file format"] = ErrorLevel.Error,
        ["Out of memory"] = ErrorLevel.Error,
        
        // 警告 - 非关键问题
        ["GPU functions for drawing are not available in background mode"] = ErrorLevel.Warning,
        ["invalid non-printable character"] = ErrorLevel.Warning,
        ["DeprecationWarning"] = ErrorLevel.Warning,
        ["Warning:"] = ErrorLevel.Warning,
        
        // 信息 - 正常输出
        ["SystemError"] = ErrorLevel.Info,
        ["SyntaxError"] = ErrorLevel.Info,
        ["Traceback"] = ErrorLevel.Info,
        ["INFO:"] = ErrorLevel.Info
    };
    
    public ErrorLevel ClassifyError(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return ErrorLevel.Ignore;
            
        foreach (var pattern in ErrorPatterns)
        {
            if (line.Contains(pattern.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pattern.Value;
            }
        }
        
        return ErrorLevel.Ignore; // 默认忽略
    }
    
    public string FormatError(string line, ErrorLevel level)
    {
        return level switch
        {
            ErrorLevel.Critical => $"CRITICAL: {line}",
            ErrorLevel.Error => $"ERROR: {line}",
            ErrorLevel.Warning => $"WARNING: {line}",
            ErrorLevel.Info => $"INFO: {line}",
            _ => line
        };
    }
}
