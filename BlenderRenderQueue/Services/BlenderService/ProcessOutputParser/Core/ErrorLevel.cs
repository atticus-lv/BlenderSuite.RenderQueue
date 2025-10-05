namespace BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Core;

/// <summary>
/// 错误级别枚举
/// </summary>
public enum ErrorLevel
{
    /// <summary>忽略 - 正常信息输出</summary>
    Ignore,
    
    /// <summary>信息 - 一般性信息</summary>
    Info,
    
    /// <summary>警告 - 非关键问题</summary>
    Warning,
    
    /// <summary>错误 - 操作失败但程序可继续</summary>
    Error,
    
    /// <summary>严重错误 - 进程崩溃或无法继续</summary>
    Critical
}
