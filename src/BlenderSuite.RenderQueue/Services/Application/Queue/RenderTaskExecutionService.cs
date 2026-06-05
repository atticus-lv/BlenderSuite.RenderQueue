using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Extensions;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Services.Application.Queue;

public sealed class RenderTaskExecutionService : IRenderTaskExecutionService
{
    private readonly ConcurrentDictionary<Guid, ExecutionContext> _contexts = new();
    private readonly IRenderLogService _logService;

    public RenderTaskExecutionService(IRenderLogService logService)
    {
        _logService = logService;
    }

    public Task StartAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost)
    {
        return RunRenderPipelineAsync(task, workerHost, resumeFromFrame: null, isResume: false, resetRetryBudget: true);
    }

    public Task ResumeAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost, int resumeFromFrame)
    {
        return RunRenderPipelineAsync(task, workerHost, resumeFromFrame, isResume: true, resetRetryBudget: false);
    }

    public Task PauseAsync(RenderTaskViewModel task)
    {
        if (!_contexts.TryGetValue(task.Id, out var context))
        {
            task.LogLine("正在暂停渲染...");
            WriteTaskEvent(task, RenderLogScope.Task, "用户请求暂停渲染。");
            task.FinalizePaused();
            return Task.CompletedTask;
        }

        context.CancellationRequested = true;
        context.PauseRequested = true;
        task.LogLine("正在暂停渲染...");
        WriteTaskEvent(task, RenderLogScope.Task, "用户请求暂停渲染。");
        task.FinalizePaused();

        Task.Run(async () =>
        {
            try
            {
                await context.WorkerHost.CancelCurrentRenderAsync();
            }
            catch
            {
                // ignored
            }
            finally
            {
                DetachContext(context);
                RemoveContextIfCurrent(task.Id, context);
            }
        }).FireAndForget(
            _logService,
            nameof(RenderTaskExecutionService),
            RenderLogScope.Task,
            "暂停渲染后台取消任务失败。");

        return Task.CompletedTask;
    }

    public void Stop(RenderTaskViewModel task)
    {
        if (!_contexts.TryGetValue(task.Id, out var context))
        {
            WriteTaskEvent(task, RenderLogScope.Task, "用户请求停止渲染。");
            task.FinalizeStopped();
            return;
        }

        context.CancellationRequested = true;
        context.PauseRequested = false;
        WriteTaskEvent(task, RenderLogScope.Task, "用户请求停止渲染。");

        Task.Run(async () =>
        {
            try
            {
                await context.WorkerHost.CancelCurrentRenderAsync();
            }
            catch (Exception ex)
            {
                task.LogLine($"停止渲染失败: {ex.Message}");
                WriteTaskEvent(task, RenderLogScope.Task, $"停止渲染失败: {ex.Message}", RenderLogLevel.Warning);
            }
            finally
            {
                DetachContext(context);
                RemoveContextIfCurrent(task.Id, context);
                task.FinalizeStopped();
            }
        }).FireAndForget(
            _logService,
            nameof(RenderTaskExecutionService),
            RenderLogScope.Task,
            "停止渲染后台取消任务失败。");
    }

    private async Task RunRenderPipelineAsync(
        RenderTaskViewModel task,
        IBlenderWorkerHost workerHost,
        int? resumeFromFrame,
        bool isResume,
        bool resetRetryBudget)
    {
        if (string.IsNullOrWhiteSpace(task.BlendFilePath))
        {
            task.NotifyMissingBlendFile();
            return;
        }

        var context = _contexts.AddOrUpdate(
            task.Id,
            _ => new ExecutionContext(task, workerHost),
            (_, existing) =>
            {
                existing.WorkerHost = workerHost;
                return existing;
            });

        try
        {
            context.RenderPipelineVersion++;
            var renderPipelineVersion = context.RenderPipelineVersion;
            context.CancellationRequested = false;
            context.PauseRequested = false;
            context.LastActivityTime = DateTime.UtcNow;
            context.WorkerExitedUnexpectedly = false;
            context.LastWorkerExitCode = 0;
            if (resetRetryBudget)
            {
                context.AutomaticRecoveryAttempts = 0;
            }

            task.BeginRenderExecution(isResume, resetRetryBudget);

            AttachContext(context);
            context.ActiveWorkerProcessGeneration = workerHost.State.ProcessGeneration;
            context.OutputParser = new RenderOutputParser();

            var request = task.BuildWorkerRequest(resumeFromFrame);
            task.SetStatusDetail(string.Empty);
            WriteTaskEvent(
                task,
                RenderLogScope.Task,
                $"{(isResume ? "恢复渲染" : "开始渲染")}: {task.DescribeWorkerRequest(request)}",
                metadata: new Dictionary<string, string>
                {
                    ["kind"] = isResume ? "resume" : "start"
                });

            var response = await ExecuteRenderWithRecoveryAsync(context, request, renderPipelineVersion);
            WriteTaskEvent(
                task,
                response.OutputVerified ? RenderLogScope.Task : RenderLogScope.Recovery,
                isResume
                    ? (response.OutputVerified ? "恢复渲染输出已校验" : "恢复渲染完成，但未能校验输出文件")
                    : (response.OutputVerified ? "渲染输出已校验" : "渲染已完成，但未能校验输出文件"),
                response.OutputVerified ? RenderLogLevel.Info : RenderLogLevel.Warning);

            EnsureRenderPipelineIsCurrent(task, context, renderPipelineVersion);
            if (task.Status == RenderTaskStatus.Running)
            {
                WriteTaskEvent(task, RenderLogScope.Task, "渲染任务完成。");
                task.FinalizeCompleted();
            }
        }
        catch (TaskCanceledException ex)
        {
            if (context.PauseRequested)
            {
                WriteTaskEvent(task, RenderLogScope.Task, isResume ? "恢复渲染已暂停。" : "渲染任务已暂停。", RenderLogLevel.Info);
                if (task.Status != RenderTaskStatus.Paused)
                {
                    task.FinalizePaused();
                }
            }
            else if (context.CancellationRequested || ex.CancellationToken.IsCancellationRequested)
            {
                WriteTaskEvent(task, RenderLogScope.Task, isResume ? "恢复渲染任务被用户取消。" : "渲染任务被用户取消。", RenderLogLevel.Warning);
                task.FinalizeCancelled(isResume ? "恢复渲染任务被用户取消" : "渲染任务被用户取消");
            }
            else
            {
                var detail = BuildFinalFailureDetail(context, ex);
                WriteTaskEvent(task, RenderLogScope.Recovery, $"{(isResume ? "恢复渲染任务" : "渲染任务")}超时: {detail}", RenderLogLevel.Error);
                task.FinalizeFailed(detail, $"{(isResume ? "恢复渲染任务" : "渲染任务")}超时: {detail}");
            }
        }
        catch (OperationCanceledException ex)
        {
            if (context.PauseRequested)
            {
                WriteTaskEvent(task, RenderLogScope.Task, isResume ? "恢复渲染已暂停。" : "渲染操作已暂停。", RenderLogLevel.Info);
                if (task.Status != RenderTaskStatus.Paused)
                {
                    task.FinalizePaused();
                }
            }
            else if (context.CancellationRequested)
            {
                WriteTaskEvent(task, RenderLogScope.Task, isResume ? "恢复渲染被用户取消。" : "渲染操作被用户取消。", RenderLogLevel.Warning);
                task.FinalizeCancelled(isResume ? "恢复渲染被用户取消" : "渲染操作被用户取消");
            }
            else
            {
                WriteTaskEvent(task, RenderLogScope.Recovery, $"{(isResume ? "恢复渲染" : "渲染")}操作被取消: {ex.Message}", RenderLogLevel.Warning);
                task.FinalizeCancelled($"{(isResume ? "恢复渲染" : "渲染")}操作被取消: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            if (context.PauseRequested)
            {
                if (task.Status != RenderTaskStatus.Paused)
                {
                    WriteTaskEvent(task, RenderLogScope.Task, isResume ? "恢复渲染已暂停。" : "渲染已暂停。", RenderLogLevel.Info);
                    task.FinalizePaused();
                }
            }
            else if (context.CancellationRequested)
            {
                if (task.Status != RenderTaskStatus.Paused)
                {
                    WriteTaskEvent(task, RenderLogScope.Task, isResume ? "恢复渲染已取消。" : "渲染已取消。", RenderLogLevel.Warning);
                    task.FinalizeCancelled(isResume ? "恢复渲染已取消" : "渲染已取消");
                }
            }
            else
            {
                var detail = BuildFinalFailureDetail(context, ex);
                WriteTaskEvent(task, RenderLogScope.Recovery, $"{(isResume ? "恢复渲染" : "渲染")}失败: {detail}", RenderLogLevel.Error);
                task.FinalizeFailed(detail, $"{(isResume ? "恢复渲染" : "渲染")}失败: {detail}");
            }
        }
        finally
        {
            if (task.Status != RenderTaskStatus.Running)
            {
                DetachContext(context);
                RemoveContextIfCurrent(task.Id, context);
            }
        }
    }

    private async Task<BlenderWorkerResponse> ExecuteRenderWithRecoveryAsync(
        ExecutionContext context,
        BlenderWorkerRequest initialRequest,
        int renderPipelineVersion)
    {
        var request = initialRequest;

        while (true)
        {
            EnsureRenderPipelineIsCurrent(context.Task, context, renderPipelineVersion);
            context.LastActivityTime = DateTime.UtcNow;
            context.WorkerExitedUnexpectedly = false;
            context.LastWorkerExitCode = 0;

            try
            {
                return await WaitForRenderCompletionAsync(context, request, renderPipelineVersion);
            }
            catch (Exception ex) when (IsRecoverableFailure(context, ex))
            {
                var resumeFrame = context.Task.GetResumeFrameForRetry(request);
                var reason = GetRecoverableFailureReason(context, ex);
                context.LastRecoveryReason = reason;
                var recovered = await TryRecoverAndRetryAsync(context, reason, resumeFrame);
                if (!recovered)
                {
                    throw new InvalidOperationException(reason, ex);
                }

                EnsureRenderPipelineIsCurrent(context.Task, context, renderPipelineVersion);
                request = context.Task.BuildWorkerRequest(resumeFrame);
                WriteTaskEvent(
                    context.Task,
                    RenderLogScope.Recovery,
                    $"自动恢复成功，继续渲染: {context.Task.DescribeWorkerRequest(request)}",
                    metadata: new Dictionary<string, string>
                    {
                        ["resume_frame"] = resumeFrame.ToString()
                    });
            }
        }
    }

    private static async Task<BlenderWorkerResponse> WaitForRenderCompletionAsync(
        ExecutionContext context,
        BlenderWorkerRequest request,
        int renderPipelineVersion)
    {
        using var renderAttemptCts = new CancellationTokenSource();
        var renderTask = context.WorkerHost.RenderTaskAsync(request, renderAttemptCts.Token);

        while (true)
        {
            EnsureRenderPipelineIsCurrent(context.Task, context, renderPipelineVersion);
            var completedTask = await Task.WhenAny(renderTask, Task.Delay(1000));
            if (completedTask == renderTask)
            {
                return await renderTask;
            }

            if (context.CancellationRequested)
            {
                renderAttemptCts.Cancel();
                await DrainCancelledRenderTaskAsync(renderTask);
                throw new OperationCanceledException("Render cancelled by user.");
            }

            if (context.WorkerExitedUnexpectedly)
            {
                renderAttemptCts.Cancel();
                await DrainCancelledRenderTaskAsync(renderTask);
                throw new InvalidOperationException($"Blender worker exited unexpectedly with code {context.LastWorkerExitCode}.");
            }

            if (context.TaskTimeoutSeconds > 0 &&
                DateTime.UtcNow - context.LastActivityTime > TimeSpan.FromSeconds(context.TaskTimeoutSeconds))
            {
                renderAttemptCts.Cancel();
                await DrainCancelledRenderTaskAsync(renderTask);
                throw new TimeoutException($"No render activity was observed for {context.TaskTimeoutSeconds} seconds.");
            }
        }
    }

    private static async Task DrainCancelledRenderTaskAsync(Task renderTask)
    {
        try
        {
            await renderTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // ignored
        }
    }

    private static bool IsRecoverableFailure(ExecutionContext context, Exception ex)
    {
        if (context.CancellationRequested)
        {
            return false;
        }

        var category = GetFailureCategory(context, ex);
        if (category is "file_error" or "script_error" or "normal_quit")
        {
            return false;
        }

        if (context.WorkerExitedUnexpectedly)
        {
            return true;
        }

        return ex switch
        {
            ArgumentException => false,
            FileNotFoundException => false,
            ObjectDisposedException => false,
            TaskCanceledException => true,
            OperationCanceledException => true,
            _ => true
        };
    }

    private static string GetRecoverableFailureReason(ExecutionContext context, Exception ex)
    {
        var category = GetFailureCategory(context, ex);
        if (category == "unexpected_exit")
        {
            return context.WorkerExitedUnexpectedly
                ? $"检测到 Blender 进程异常退出 (退出码 {context.LastWorkerExitCode})"
                : "检测到 Blender Worker 异常退出";
        }

        if (context.WorkerExitedUnexpectedly)
        {
            return $"检测到 Blender 进程崩溃 (退出码 {context.LastWorkerExitCode})";
        }

        if (ex is TimeoutException)
        {
            return $"渲染超过 {context.TaskTimeoutSeconds} 秒无活动";
        }

        if (ex is OperationCanceledException or TaskCanceledException)
        {
            return "渲染请求意外中断";
        }

        if (ex.Message.Contains("no render output was verified", StringComparison.OrdinalIgnoreCase))
        {
            return "渲染输出校验失败";
        }

        return $"渲染执行异常: {ex.Message}";
    }

    private static string BuildFinalFailureDetail(ExecutionContext context, Exception ex)
    {
        var category = GetFailureCategory(context, ex);
        return category switch
        {
            "file_error" => RenderTaskViewModel.FormatLocalized("RenderTask_StatusDetail_FileError", ex.Message),
            "script_error" => RenderTaskViewModel.FormatLocalized("RenderTask_StatusDetail_ScriptError", ex.Message),
            "normal_quit" => BlenderSuite.RenderQueue.Localizer.Localizer.Instance["RenderTask_StatusDetail_NormalQuit"],
            "unexpected_exit" => context.WorkerExitedUnexpectedly
                ? RenderTaskViewModel.FormatLocalized("RenderTask_StatusDetail_UnexpectedExitWithCode", context.LastWorkerExitCode)
                : BlenderSuite.RenderQueue.Localizer.Localizer.Instance["RenderTask_StatusDetail_UnexpectedExit"],
            "render_timeout" => RenderTaskViewModel.FormatLocalized("RenderTask_StatusDetail_RenderTimeout", context.TaskTimeoutSeconds),
            "output_verification_failure" => BlenderSuite.RenderQueue.Localizer.Localizer.Instance["RenderTask_StatusDetail_OutputVerificationFailed"],
            "request_interrupted" => BlenderSuite.RenderQueue.Localizer.Localizer.Instance["RenderTask_StatusDetail_RequestInterrupted"],
            _ => string.IsNullOrWhiteSpace(ex.Message)
                ? BlenderSuite.RenderQueue.Localizer.Localizer.Instance["RenderTask_StatusDetail_RenderFailed"]
                : ex.Message
        };
    }

    private static string GetFailureCategory(ExecutionContext context, Exception ex)
    {
        if (context.WorkerExitedUnexpectedly)
        {
            var workerCategory = context.WorkerHost.State.LastErrorCategory;
            return string.IsNullOrWhiteSpace(workerCategory) ? "unexpected_exit" : workerCategory;
        }

        if (ex is TimeoutException)
        {
            return "render_timeout";
        }

        if (ex is OperationCanceledException or TaskCanceledException)
        {
            return "request_interrupted";
        }

        if (ex.Message.Contains("no render output was verified", StringComparison.OrdinalIgnoreCase))
        {
            return "output_verification_failure";
        }

        var hostCategory = context.WorkerHost.State.LastErrorCategory;
        if (!string.IsNullOrWhiteSpace(hostCategory))
        {
            return hostCategory;
        }

        var normalized = ex.Message.ToLowerInvariant();
        if (normalized.Contains("file format is not supported") ||
            normalized.Contains("not a blend file") ||
            normalized.Contains("cannot read file as a blender file"))
        {
            return "file_error";
        }

        if (normalized.Contains("scene '") && normalized.Contains("was not found"))
        {
            return "script_error";
        }

        return string.Empty;
    }

    private async Task<bool> TryRecoverAndRetryAsync(ExecutionContext context, string reason, int resumeFrame)
    {
        if (context.AutomaticRecoveryAttempts >= context.MaxRetryAttempts)
        {
            WriteTaskEvent(context.Task, RenderLogScope.Recovery, $"{reason}，已达到最大自动重试次数 ({context.MaxRetryAttempts})。", RenderLogLevel.Error);
            return false;
        }

        context.AutomaticRecoveryAttempts++;
        context.Task.SetStatusDetail(RenderTaskViewModel.FormatLocalized(
            "RenderTask_StatusDetail_RecoveringWorker",
            context.AutomaticRecoveryAttempts,
            context.MaxRetryAttempts));
        WriteTaskEvent(
            context.Task,
            RenderLogScope.Recovery,
            $"{reason}，尝试自动恢复 ({context.AutomaticRecoveryAttempts}/{context.MaxRetryAttempts})...",
            RenderLogLevel.Warning);

        try
        {
            context.SuppressUnexpectedExitHandling = true;
            var recoveryResult = await context.WorkerHost.RecoverAsync();
            WriteTaskEvent(
                context.Task,
                RenderLogScope.Recovery,
                recoveryResult.Message,
                recoveryResult.Recovered ? RenderLogLevel.Info : RenderLogLevel.Error);
            if (!recoveryResult.Recovered)
            {
                return false;
            }

            context.ActiveWorkerProcessGeneration = context.WorkerHost.State.ProcessGeneration;
        }
        catch (Exception ex)
        {
            WriteTaskEvent(context.Task, RenderLogScope.Recovery, $"自动恢复失败: {ex.Message}", RenderLogLevel.Error);
            return false;
        }
        finally
        {
            context.SuppressUnexpectedExitHandling = false;
            context.WorkerExitedUnexpectedly = false;
            context.LastWorkerExitCode = 0;
            context.LastActivityTime = DateTime.UtcNow;
        }

        context.Task.CurrentFrame = resumeFrame;
        context.Task.SetStatusDetail(RenderTaskViewModel.FormatLocalized(
            "RenderTask_StatusDetail_RecoverySucceeded",
            context.AutomaticRecoveryAttempts,
            context.MaxRetryAttempts));
        return true;
    }

    private void AttachContext(ExecutionContext context)
    {
        DetachContext(context);

        context.OutputHandler = line =>
        {
            context.LastActivityTime = DateTime.UtcNow;
            context.Task.HandleRawOutputLine(line, context.OutputParser);
        };

        context.ErrorHandler = line =>
        {
            context.LastActivityTime = DateTime.UtcNow;
            context.Task.HandleRawErrorLine(line);
        };

        context.ExitHandler = exitCode =>
        {
            if (context.Task.Status != RenderTaskStatus.Running)
            {
                return;
            }

            if (context.WorkerHost.State.ProcessGeneration != context.ActiveWorkerProcessGeneration)
            {
                return;
            }

            WriteTaskEvent(
                context.Task,
                RenderLogScope.Worker,
                $"Blender 进程异常退出，退出码: {exitCode}",
                exitCode == 0 ? RenderLogLevel.Warning : RenderLogLevel.Error,
                metadata: new Dictionary<string, string>
                {
                    ["kind"] = "worker_exit",
                    ["exit_code"] = exitCode.ToString()
                });
            context.LastActivityTime = DateTime.UtcNow;

            if (context.CancellationRequested || context.SuppressUnexpectedExitHandling || exitCode == 0)
            {
                return;
            }

            context.WorkerExitedUnexpectedly = true;
            context.LastWorkerExitCode = exitCode;
        };

        context.WorkerHost.OnOutputReceived += context.OutputHandler;
        context.WorkerHost.OnErrorReceived += context.ErrorHandler;
        context.WorkerHost.OnProcessExited += context.ExitHandler;
    }

    private void DetachContext(ExecutionContext context)
    {
        if (context.OutputHandler != null)
        {
            context.WorkerHost.OnOutputReceived -= context.OutputHandler;
        }

        if (context.ErrorHandler != null)
        {
            context.WorkerHost.OnErrorReceived -= context.ErrorHandler;
        }

        if (context.ExitHandler != null)
        {
            context.WorkerHost.OnProcessExited -= context.ExitHandler;
        }

        context.OutputHandler = null;
        context.ErrorHandler = null;
        context.ExitHandler = null;
    }

    private void RemoveContextIfCurrent(Guid taskId, ExecutionContext context)
    {
        if (_contexts.TryGetValue(taskId, out var current) && ReferenceEquals(current, context))
        {
            _contexts.TryRemove(taskId, out _);
        }
    }

    private static void EnsureRenderPipelineIsCurrent(RenderTaskViewModel task, ExecutionContext context, int renderPipelineVersion)
    {
        if (task.Status != RenderTaskStatus.Running)
        {
            throw new OperationCanceledException("Render pipeline is no longer active.");
        }

        if (context.RenderPipelineVersion != renderPipelineVersion)
        {
            throw new OperationCanceledException("Render pipeline is no longer active.");
        }
    }

    private sealed class ExecutionContext
    {
        public ExecutionContext(RenderTaskViewModel task, IBlenderWorkerHost workerHost)
        {
            Task = task;
            WorkerHost = workerHost;
            OutputParser = new RenderOutputParser();
        }

        public RenderTaskViewModel Task { get; }
        public IBlenderWorkerHost WorkerHost { get; set; }
        public IRenderOutputParser OutputParser { get; set; }
        public bool CancellationRequested { get; set; }
        public bool PauseRequested { get; set; }
        public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
        public int AutomaticRecoveryAttempts { get; set; }
        public bool WorkerExitedUnexpectedly { get; set; }
        public int LastWorkerExitCode { get; set; }
        public bool SuppressUnexpectedExitHandling { get; set; }
        public int RenderPipelineVersion { get; set; }
        public long ActiveWorkerProcessGeneration { get; set; }
        public string LastRecoveryReason { get; set; } = string.Empty;
        public Action<string>? OutputHandler { get; set; }
        public Action<string>? ErrorHandler { get; set; }
        public Action<int>? ExitHandler { get; set; }
        public int TaskTimeoutSeconds => Task.GetGlobalRenderTimeoutSeconds();
        public int MaxRetryAttempts => Task.GetGlobalMaxRetryAttempts();
    }

    private void WriteTaskEvent(
        RenderTaskViewModel task,
        RenderLogScope scope,
        string message,
        RenderLogLevel level = RenderLogLevel.Info,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        _logService.Write(level, scope, message, task.Id, task.BlendFilePath, nameof(RenderTaskExecutionService), metadata);
    }
}
