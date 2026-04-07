using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.ViewModels;
using Xunit;

namespace BlenderRenderQueue.Tests.Services.Application.Queue;

public sealed class RenderTaskExecutionServiceTests
{
    [AvaloniaFact]
    public async Task StartAsync_CompletesTask_AndProjectsProgressFromWorkerOutput()
    {
        using var tempBlend = TemporaryFile.Create(".blend");
        var task = new RenderTaskViewModel(tempBlend.Path, 1, 1, animation: false);
        var workerHost = new FakeBlenderWorkerHost();
        var logService = TestLogServiceFactory.Create();
        task.AttachLogService(logService);
        workerHost.RenderTaskHandler = async (request, cancellationToken) =>
        {
            workerHost.EmitOutput("Rendering single frame (frame 1)");
            workerHost.EmitOutput("Rendering frame 1");
            workerHost.EmitOutput("Start rendering: Scene, ViewLayer");
            workerHost.EmitOutput("Engine: Cycles");
            workerHost.EmitOutput("Mem: 512M | Sample 8/8");
            await Task.Delay(20);
            return new BlenderWorkerResponse
            {
                Ok = true,
                WorkerState = "completed",
                OutputVerified = true
            };
        };

        var sut = new RenderTaskExecutionService(logService);

        await sut.StartAsync(task, workerHost);
        await DrainUiAsync();

        Assert.Equal(RenderTaskStatus.Completed, task.Status);
        Assert.Equal("8/8", task.SampleText);
        Assert.Equal(1.0, task.Progress01, 3);
        Assert.Equal(1.0, task.OverallProgress01, 3);
        Assert.NotNull(task.EndTime);

        task.Dispose();
    }

    [AvaloniaFact]
    public async Task StartAsync_RecoversUnexpectedExit_AndRetriesWithinSameTask()
    {
        using var tempBlend = TemporaryFile.Create(".blend");
        var task = new RenderTaskViewModel(tempBlend.Path, 1, 1, animation: false);
        task.SetGlobalMaxRetryAttempts(2);
        var logService = TestLogServiceFactory.Create();
        task.AttachLogService(logService);

        var firstCall = true;
        var workerHost = new FakeBlenderWorkerHost();
        workerHost.RenderTaskHandler = async (request, cancellationToken) =>
        {
            if (firstCall)
            {
                firstCall = false;
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await Task.Delay(150);
                    workerHost.State.LastErrorCategory = "unexpected_exit";
                    workerHost.EmitExit(139);
                });

                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new OperationCanceledException(cancellationToken);
            }

            workerHost.EmitOutput("Rendering single frame (frame 1)");
            workerHost.EmitOutput("Rendering frame 1");
            workerHost.EmitOutput("Engine: Cycles");
            workerHost.EmitOutput("Mem: 256M | Sample 4/4");
            return new BlenderWorkerResponse
            {
                Ok = true,
                WorkerState = "completed",
                OutputVerified = true
            };
        };

        var sut = new RenderTaskExecutionService(logService);

        await sut.StartAsync(task, workerHost);
        await DrainUiAsync();

        Assert.Equal(RenderTaskStatus.Completed, task.Status);
        Assert.Equal(1, workerHost.RecoverCalls);
        Assert.Equal(2, workerHost.RenderTaskCalls);

        task.Dispose();
    }

    [AvaloniaFact]
    public async Task StartAsync_DoesNotRecover_ForFileErrors()
    {
        using var tempBlend = TemporaryFile.Create(".blend");
        var task = new RenderTaskViewModel(tempBlend.Path, 1, 1, animation: false);
        task.SetGlobalMaxRetryAttempts(3);
        var logService = TestLogServiceFactory.Create();
        task.AttachLogService(logService);

        var workerHost = new FakeBlenderWorkerHost();
        workerHost.RenderTaskHandler = (request, cancellationToken) =>
        {
            workerHost.State.LastErrorCategory = "file_error";
            throw new InvalidOperationException("File format is not supported");
        };

        var sut = new RenderTaskExecutionService(logService);

        await sut.StartAsync(task, workerHost);
        await DrainUiAsync();

        Assert.Equal(RenderTaskStatus.Failed, task.Status);
        Assert.Equal(0, workerHost.RecoverCalls);
        Assert.Equal(1, workerHost.RenderTaskCalls);
        Assert.False(string.IsNullOrWhiteSpace(task.StatusDetailText));

        task.Dispose();
    }

    [AvaloniaFact]
    public async Task PauseAsync_KeepsTaskInPausedState()
    {
        using var tempBlend = TemporaryFile.Create(".blend");
        var task = new RenderTaskViewModel(tempBlend.Path, 1, 1, animation: false);
        var logService = TestLogServiceFactory.Create();
        task.AttachLogService(logService);

        var workerHost = new FakeBlenderWorkerHost();
        workerHost.RenderTaskHandler = async (request, cancellationToken) =>
        {
            workerHost.EmitOutput("Rendering single frame (frame 1)");
            workerHost.EmitOutput("Rendering frame 1");
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new BlenderWorkerResponse
            {
                Ok = true,
                WorkerState = "completed",
                OutputVerified = true
            };
        };

        var sut = new RenderTaskExecutionService(logService);

        var startTask = sut.StartAsync(task, workerHost);
        await WaitUntilAsync(() => task.Status == RenderTaskStatus.Running);

        await sut.PauseAsync(task);
        await startTask;
        await DrainUiAsync();

        Assert.Equal(RenderTaskStatus.Paused, task.Status);
        Assert.Equal(1, workerHost.CancelCalls);
        Assert.Equal(0, GetExecutionContextCount(sut));

        task.Dispose();
    }

    [AvaloniaFact]
    public async Task StartAsync_RemovesExecutionContext_AfterCompletion()
    {
        using var tempBlend = TemporaryFile.Create(".blend");
        var task = new RenderTaskViewModel(tempBlend.Path, 1, 1, animation: false);
        var workerHost = new FakeBlenderWorkerHost();
        var logService = TestLogServiceFactory.Create();
        task.AttachLogService(logService);

        var sut = new RenderTaskExecutionService(logService);

        await sut.StartAsync(task, workerHost);
        await DrainUiAsync();

        Assert.Equal(RenderTaskStatus.Completed, task.Status);
        Assert.Equal(0, GetExecutionContextCount(sut));

        task.Dispose();
    }

    [AvaloniaFact]
    public async Task RefreshFilePropertiesAsync_WithRealBlender_DoesNotCrash_AndLoadsSceneData()
    {
        var blenderPath = "/Applications/Blender.app/Contents/MacOS/Blender";
        var blendFilePath = "/tmp/brq-anim-square-fixed.blend";
        if (!OperatingSystem.IsMacOS() || !File.Exists(blenderPath) || !File.Exists(blendFilePath))
        {
            return;
        }

        var task = new RenderTaskViewModel(blendFilePath, 1, 1, animation: false);
        var logService = TestLogServiceFactory.Create();
        task.AttachLogService(logService);

        await task.LoadFilePropertiesAsync(blenderPath);
        await task.RefreshFilePropertiesAsync(blenderPath);
        await DrainUiAsync();

        Assert.True(task.ScenePropertiesView.SelectedSceneProperties.IsLoaded);
        Assert.NotEmpty(task.ScenePropertiesView.SceneNames);
        Assert.False(task.ScenePropertiesView.IsLoading);

        task.Dispose();
    }

    private static Task DrainUiAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(predicate(), "Condition was not met within the allotted timeout.");
    }

    private static int GetExecutionContextCount(RenderTaskExecutionService sut)
    {
        var field = typeof(RenderTaskExecutionService).GetField("_contexts", BindingFlags.Instance | BindingFlags.NonPublic);
        var contexts = field?.GetValue(sut);
        Assert.NotNull(contexts);
        var countProperty = contexts.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        return Assert.IsType<int>(countProperty?.GetValue(contexts));
    }
}
