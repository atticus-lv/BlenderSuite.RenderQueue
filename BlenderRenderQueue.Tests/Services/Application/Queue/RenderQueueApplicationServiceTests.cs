using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.Services.Business.Submission;
using BlenderRenderQueue.ViewModels;
using Xunit;

namespace BlenderRenderQueue.Tests.Services.Application.Queue;

public sealed class RenderQueueApplicationServiceTests
{
    [AvaloniaFact]
    public async Task SubmitTaskAsync_AddsTaskAndPublishesSnapshotWithoutViewModelBridge()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService);

        var response = await sut.SubmitTaskAsync(new LocalSubmissionRequest
        {
            Filepath = blendFile.Path,
            Filename = Path.GetFileName(blendFile.Path),
            SceneName = "SceneA",
            OverrideFrameRange = true,
            FrameStart = 3,
            FrameEnd = 5
        });
        await DrainUiAsync();

        Assert.True(response.Ok);
        Assert.Single(sut.RenderTasks);
        var task = sut.RenderTasks.Single();
        Assert.Equal("SceneA", task.SelectedSceneName);
        Assert.True(task.OverrideFrameRange);
        Assert.Equal(3, task.StartFrame);
        Assert.Equal(5, task.EndFrame);
        Assert.Single(sut.Snapshot.Tasks);
        Assert.Equal(task.Id, sut.Snapshot.Tasks[0].TaskId);
        Assert.Equal(QueueExecutionState.Idle, sut.Snapshot.State);
    }

    [AvaloniaFact]
    public async Task StartQueueFromSubmissionAsync_StartsExecutionThroughApplicationService()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        using var blenderExecutable = TemporaryFile.Create(".exe");

        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService);
        sut.SetBlenderPath(blenderExecutable.Path);

        await sut.SubmitTaskAsync(new LocalSubmissionRequest
        {
            Filepath = blendFile.Path,
            Filename = Path.GetFileName(blendFile.Path),
            SceneName = string.Empty,
            OverrideFrameRange = false,
            FrameStart = 1,
            FrameEnd = 1
        });

        var response = await sut.StartQueueFromSubmissionAsync();
        await WaitUntilAsync(() => executionService.StartCalls == 1);
        await WaitUntilAsync(() => sut.Snapshot.State == QueueExecutionState.Completed);
        await DrainUiAsync();

        Assert.True(response.Ok);
        Assert.Equal(1, workerHost.EnsureReadyCalls);
        Assert.Equal(1, executionService.StartCalls);
        Assert.Equal(QueueExecutionState.Completed, sut.Snapshot.State);
        Assert.Equal(1.0, sut.Snapshot.OverallProgress01, 3);
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

    private static Task DrainUiAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    }
}
