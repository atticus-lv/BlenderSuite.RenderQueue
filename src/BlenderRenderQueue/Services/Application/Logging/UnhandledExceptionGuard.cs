using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderRenderQueue.Services.Application.Logging;

public static class UnhandledExceptionGuard
{
    private static readonly object RegisterLock = new();
    private static readonly object FallbackLogLock = new();
    private static bool _registered;

    public static void Register()
    {
        lock (RegisterLock)
        {
            if (_registered)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
            _registered = true;
        }
    }

    public static void WriteObservedTaskException(
        Exception exception,
        string source,
        RenderLogScope scope = RenderLogScope.System,
        string? message = null)
    {
        WriteException(exception, message ?? "后台任务发生未观测异常。", source, scope);
    }

    public static void WriteHandledException(
        Exception exception,
        string source,
        RenderLogScope scope = RenderLogScope.System,
        string? message = null)
    {
        WriteException(exception, message ?? "已捕获异常。", source, scope);
    }

    public static void WriteFallback(Exception exception, string source, string message)
    {
        WriteFallback($"{DateTimeOffset.UtcNow:O} [{source}] {message}{Environment.NewLine}{exception}");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException($"Unhandled non-Exception object: {e.ExceptionObject}");
        WriteException(exception, "进程发生未捕获异常。", nameof(AppDomain), RenderLogScope.System);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteException(e.Exception, "TaskScheduler 发现未观测任务异常。", nameof(TaskScheduler), RenderLogScope.System);
        e.SetObserved();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteException(e.Exception, "Avalonia UI 线程发生未处理异常。", "Dispatcher.UIThread", RenderLogScope.System);
        e.Handled = false;
    }

    private static void WriteException(Exception exception, string message, string source, RenderLogScope scope)
    {
        try
        {
            var logService = AppServices.Instance.GetService<IRenderLogService>();
            if (logService != null)
            {
                logService.Write(
                    RenderLogLevel.Error,
                    scope,
                    $"{message}{Environment.NewLine}{exception}",
                    source: source);
                return;
            }
        }
        catch (Exception fallbackException)
        {
            WriteFallback($"{DateTimeOffset.UtcNow:O} [UnhandledExceptionGuard] 写入结构化日志失败。{Environment.NewLine}{fallbackException}");
        }

        WriteFallback(exception, source, message);
    }

    private static void WriteFallback(string text)
    {
        try
        {
            Console.Error.WriteLine(text);
            Console.Error.Flush();
        }
        catch
        {
            // Console may be unavailable for WinExe startup crashes.
        }

        try
        {
            lock (FallbackLogLock)
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BlenderRenderQueue");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "unhandled-exceptions.log"), text + Environment.NewLine);
            }
        }
        catch
        {
            // Last-resort logging must never throw back into an exception handler.
        }
    }
}
