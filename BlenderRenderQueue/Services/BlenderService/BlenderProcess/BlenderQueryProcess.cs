using System;

namespace BlenderRenderQueue.Services.BlenderService.BlenderProcess;

/// <summary>
/// Blender查询进程 - 用于查询文件属性、版本信息等
/// </summary>
public class BlenderQueryProcess : BaseBlenderProcess
{
    public override BlenderProcessType ProcessType => BlenderProcessType.Query;

    public BlenderQueryProcess(string blenderPath) 
        : base(blenderPath, BlenderProcessConfig.CreateQueryConfig())
    {
    }

    /// <summary>
    /// 查询进程特有的错误处理逻辑
    /// </summary>
    protected override void HandleErrorOutput(string errorData)
    {
        // 查询进程对错误更敏感，大部分错误都应该报告
        var isBlenderCrash = errorData.Contains("Blender quit", StringComparison.OrdinalIgnoreCase);
        var isAccessViolationCrash = errorData.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase);
        
        if (isBlenderCrash || isAccessViolationCrash)
        {
            RaiseErrorReceived($"Error: {errorData}");
        }
        else
        {
            // 过滤非关键警告
            var isNonCriticalWarning = errorData.Contains("GPU functions for drawing are not available in background mode") ||
                                     errorData.Contains("invalid non-printable character U+FEFF") ||
                                     errorData.Contains("SystemError") ||
                                     errorData.Contains("SyntaxError") ||
                                     errorData.Contains("Traceback") ||
                                     errorData.Contains("Warning") ||
                                     errorData.Contains("DeprecationWarning");
            
            if (isNonCriticalWarning)
            {
                RaiseOutputReceived($"[INFO] {errorData}");
            }
            else
            {
                RaiseErrorReceived(errorData);
            }
        }
    }
}
