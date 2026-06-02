using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderRenderQueue.Services.Application.Logging;

internal static class ApplicationLogWriter
{
    public static void Write(
        RenderLogLevel level,
        RenderLogScope scope,
        string message,
        string source,
        Guid? taskId = null,
        string? blendFilePath = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        try
        {
            AppServices.Instance.GetService<IRenderLogService>()?.Write(
                level,
                scope,
                message,
                taskId,
                blendFilePath,
                source,
                metadata);
        }
        catch (Exception ex)
        {
            UnhandledExceptionGuard.WriteFallback(
                ex,
                source,
                $"写入结构化日志失败。原始消息: {message}");
        }
    }
}
