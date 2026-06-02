using System;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Extensions;

public static class TaskExtensions
{
    public static void FireAndForget(
        this Task task,
        IRenderLogService? logService = null,
        string? source = null,
        RenderLogScope scope = RenderLogScope.System,
        string? message = null,
        bool ignoreCancellation = true)
    {
        source ??= "FireAndForget";

        var continuation = task.ContinueWith(
            completedTask =>
            {
                try
                {
                    if (completedTask.IsCanceled)
                    {
                        if (!ignoreCancellation)
                        {
                            logService?.Write(
                                RenderLogLevel.Warning,
                                scope,
                                message ?? "后台任务已取消。",
                                source: source);
                        }

                        return;
                    }

                    var exception = completedTask.Exception?.GetBaseException();
                    if (exception == null)
                    {
                        return;
                    }

                    if (ignoreCancellation && exception is OperationCanceledException)
                    {
                        return;
                    }

                    if (logService != null)
                    {
                        logService.Write(
                            RenderLogLevel.Error,
                            scope,
                            $"{message ?? "后台任务发生未观测异常。"}{Environment.NewLine}{exception}",
                            source: source);
                        return;
                    }

                    UnhandledExceptionGuard.WriteObservedTaskException(
                        exception,
                        source,
                        scope,
                        message);
                }
                catch (Exception observerException)
                {
                    UnhandledExceptionGuard.WriteFallback(
                        observerException,
                        source,
                        $"记录后台任务异常时发生错误。原始后台任务异常:{Environment.NewLine}{completedTask.Exception?.GetBaseException()}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        GC.KeepAlive(continuation);
    }
}
