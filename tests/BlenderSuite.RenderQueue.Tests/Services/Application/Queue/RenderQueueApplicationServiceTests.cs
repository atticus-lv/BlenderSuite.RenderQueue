using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.Application.Queue;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using BlenderSuite.RenderQueue.Services.Business.Persistence;
using BlenderSuite.RenderQueue.Services.Business.Submission;
using BlenderSuite.RenderQueue.ViewModels;
using Xunit;

namespace BlenderSuite.RenderQueue.Tests.Services.Application.Queue;

public sealed class RenderQueueApplicationServiceTests
{
    private static IRenderTaskFactory CreateTaskFactory(IRenderLogService logService)
    {
        return new RenderTaskFactory(new FakeBlenderQueryService(), logService);
    }

    [AvaloniaFact]
    public async Task SubmitTaskAsync_AddsTaskAndPublishesSnapshotWithoutViewModelBridge()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));

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
    public async Task SubmitTaskAsync_PreservesSubmittedSceneFrameRangeWhenNotOverride()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));

        var response = await sut.SubmitTaskAsync(new LocalSubmissionRequest
        {
            Filepath = blendFile.Path,
            Filename = Path.GetFileName(blendFile.Path),
            SceneName = "Scene",
            OverrideFrameRange = false,
            FrameStart = 1,
            FrameEnd = 120
        });
        await DrainUiAsync();

        Assert.True(response.Ok);
        var task = Assert.Single(sut.RenderTasks);
        Assert.False(task.OverrideFrameRange);
        Assert.Equal(1, task.StartFrame);
        Assert.Equal(120, task.EndFrame);
        Assert.True(task.Animation);
    }

    [AvaloniaFact]
    public void BuildWorkerRequest_UsesLoadedSceneFrameRangeWhenAnimationIsEnabled()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        var scenePropertiesView = new BlendScenePropertiesViewModel(new FakeBlenderQueryService());
        var task = new RenderTaskViewModel(
            scenePropertiesView,
            new RenderTaskInfo
            {
                Id = Guid.NewGuid(),
                Filename = Path.GetFileName(blendFile.Path),
                Filepath = blendFile.Path,
                StartFrame = 1,
                EndFrame = 1,
                Enable = true,
                Override = new OverrideData
                {
                    OverrideScene = new OverrideSceneData
                    {
                        SceneName = "Scene"
                    }
                }
            });

        var scene = new BlendSceneProperties
        {
            FilePath = blendFile.Path,
            SceneName = "Scene",
            IsDefaultScene = true,
            FrameStart = 1,
            FrameEnd = 120
        };
        scenePropertiesView.AllScenes = new()
        {
            ["Scene"] = scene
        };
        scenePropertiesView.SelectedScene = scene;
        task.Animation = true;

        var request = task.BuildWorkerRequest();

        Assert.True(request.Animation);
        Assert.Null(request.SingleFrame);
        Assert.Equal(1, request.FrameStart);
        Assert.Equal(120, request.FrameEnd);
    }

    [AvaloniaFact]
    public void BuildWorkerRequest_UsesSingleFrameWhenAnimationIsDisabled()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        var scenePropertiesView = new BlendScenePropertiesViewModel(new FakeBlenderQueryService());
        var task = new RenderTaskViewModel(
            scenePropertiesView,
            new RenderTaskInfo
            {
                Id = Guid.NewGuid(),
                Filename = Path.GetFileName(blendFile.Path),
                Filepath = blendFile.Path,
                StartFrame = 1,
                EndFrame = 120,
                Enable = true
            });

        var scene = new BlendSceneProperties
        {
            FilePath = blendFile.Path,
            SceneName = "Scene",
            FrameStart = 1,
            FrameEnd = 120
        };
        scenePropertiesView.AllScenes = new()
        {
            ["Scene"] = scene
        };
        scenePropertiesView.SelectedScene = scene;
        task.Animation = false;

        var request = task.BuildWorkerRequest();

        Assert.False(request.Animation);
        Assert.Equal(1, request.SingleFrame);
        Assert.Null(request.FrameStart);
        Assert.Null(request.FrameEnd);
    }

    [AvaloniaFact]
    public async Task LoadFilePropertiesAsync_DerivesAnimationFromLoadedSceneRange()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        var scenePropertiesView = new BlendScenePropertiesViewModel(new FakeBlenderQueryService(frameEnd: 120));
        var task = new RenderTaskViewModel(
            scenePropertiesView,
            new RenderTaskInfo
            {
                Id = Guid.NewGuid(),
                Filename = Path.GetFileName(blendFile.Path),
                Filepath = blendFile.Path,
                StartFrame = 1,
                EndFrame = 1,
                Enable = true,
                Override = new OverrideData
                {
                    OverrideScene = new OverrideSceneData
                    {
                        SceneName = "Scene"
                    }
                }
            });

        await task.LoadFilePropertiesAsync("blender");

        Assert.True(task.Animation);
        var request = task.BuildWorkerRequest();
        Assert.True(request.Animation);
        Assert.Equal(1, request.FrameStart);
        Assert.Equal(120, request.FrameEnd);
    }

    [AvaloniaFact]
    public async Task RefreshFilePropertiesAsync_PreservesDisabledAnimationChoice()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        var scenePropertiesView = new BlendScenePropertiesViewModel(new FakeBlenderQueryService(frameEnd: 120));
        var task = new RenderTaskViewModel(
            scenePropertiesView,
            new RenderTaskInfo
            {
                Id = Guid.NewGuid(),
                Filename = Path.GetFileName(blendFile.Path),
                Filepath = blendFile.Path,
                StartFrame = 1,
                EndFrame = 120,
                Enable = true
            })
        {
            Animation = false
        };

        await task.RefreshFilePropertiesAsync("blender");

        Assert.False(task.Animation);
        var request = task.BuildWorkerRequest();
        Assert.False(request.Animation);
        Assert.Equal(1, request.SingleFrame);
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
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));
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

    [AvaloniaFact]
    public async Task StartQueueAsync_ShutsDownWorker_WhenQueueCompletes()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        using var blenderExecutable = TemporaryFile.Create(".exe");

        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));
        sut.SetBlenderPath(blenderExecutable.Path);
        sut.AddDroppedFiles([blendFile.Path]);
        await WaitUntilAsync(() => workerHost.ShutdownCalls >= 1);
        var shutdownCallsBeforeQueue = workerHost.ShutdownCalls;

        await sut.StartQueueAsync();
        await WaitUntilAsync(() => sut.Snapshot.State == QueueExecutionState.Completed);
        await WaitUntilAsync(() => workerHost.ShutdownCalls > shutdownCallsBeforeQueue);

        Assert.False(workerHost.State.IsProcessRunning);
        Assert.Equal("stopped", workerHost.State.Status);
    }

    [AvaloniaFact]
    public void AddDroppedFiles_QueuesOnlyExistingBlendFiles()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        using var otherFile = TemporaryFile.Create(".txt");
        using var blenderExecutable = TemporaryFile.Create(".exe");

        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));
        sut.SetBlenderPath(blenderExecutable.Path);

        sut.AddDroppedFiles([blendFile.Path, otherFile.Path, "/missing/file.blend"]);

        Assert.Single(sut.RenderTasks);
        Assert.Equal(blendFile.Path, sut.RenderTasks[0].BlendFilePath);
    }

    [AvaloniaFact]
    public async Task SetBlenderPath_RefreshesExistingTasksVideoCapability()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        using var blenderExecutable = TemporaryFile.Create(".exe");

        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));

        persistenceService.LoadedData = new AppData
        {
            RenderQueue =
            [
                new RenderTaskData
                {
                    RenderTask = new RenderTaskInfo
                    {
                        Id = Guid.NewGuid(),
                        Filename = Path.GetFileName(blendFile.Path),
                        Filepath = blendFile.Path,
                        StartFrame = 1,
                        EndFrame = 1,
                        Enable = true
                    }
                }
            ]
        };
        await sut.LoadQueueDataAsync();
        var task = sut.RenderTasks.Single();
        task.ScenePropertiesView.SelectedScene = new BlendSceneProperties
        {
            FilePath = blendFile.Path,
            FramePath = "/tmp/render/frame_####.png"
        };

        Assert.False(task.CanGenerateVideo);

        sut.SetBlenderPath(blenderExecutable.Path);

        Assert.True(task.CanGenerateVideo);

        sut.SetBlenderPath(string.Empty);

        Assert.False(task.CanGenerateVideo);
    }

    [AvaloniaFact]
    public async Task LoadQueueDataAsync_DoesNotDuplicate_TaskAlreadySubmittedDuringStartup()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        using var blenderExecutable = TemporaryFile.Create(".exe");

        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));

        sut.SetBlenderPath(blenderExecutable.Path);
        sut.AddBlendFiles([blendFile.Path]);
        var taskId = sut.RenderTasks.Single().Id;
        persistenceService.LoadedData = new AppData
        {
            RenderQueue =
            [
                new RenderTaskData
                {
                    RenderTask = new RenderTaskInfo
                    {
                        Id = taskId,
                        Filename = Path.GetFileName(blendFile.Path),
                        Filepath = blendFile.Path,
                        StartFrame = 1,
                        EndFrame = 1,
                        Enable = true
                    }
                }
            ]
        };

        await sut.LoadQueueDataAsync();
        await DrainUiAsync();

        Assert.Single(sut.RenderTasks);
        Assert.Equal(taskId, sut.RenderTasks.Single().Id);
    }

    [AvaloniaFact]
    public async Task StartQueueAsync_ConcurrentCalls_DoNotStartSameTaskTwice()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        using var blenderExecutable = TemporaryFile.Create(".exe");

        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));
        sut.SetBlenderPath(blenderExecutable.Path);
        sut.AddDroppedFiles([blendFile.Path]);

        var unblockExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        executionService.StartHandler = async (task, host) =>
        {
            task.BeginRenderExecution(isResume: false, resetRetryBudget: true);
            await unblockExecution.Task;
            task.FinalizeCompleted();
        };

        await Task.WhenAll(sut.StartQueueAsync(), sut.StartQueueAsync());
        await WaitUntilAsync(() => executionService.StartCalls >= 1);

        Assert.Equal(1, executionService.StartCalls);

        unblockExecution.SetResult();
        await WaitUntilAsync(() => sut.Snapshot.State == QueueExecutionState.Completed);
    }

    [AvaloniaFact]
    public async Task StartQueueAsync_ReschedulesPausedTaskWhenQueueIsStillRunning()
    {
        using var blendFile = TemporaryFile.Create(".blend");
        using var blenderExecutable = TemporaryFile.Create(".exe");

        var workerHost = new FakeBlenderWorkerHost();
        var executionService = new FakeRenderTaskExecutionService();
        var persistenceService = new FakeDataPersistenceService();
        var logService = TestLogServiceFactory.Create();
        using var sut = new RenderQueueApplicationService(workerHost, executionService, persistenceService, logService, CreateTaskFactory(logService));
        sut.SetBlenderPath(blenderExecutable.Path);
        sut.AddDroppedFiles([blendFile.Path]);

        executionService.StartHandler = (task, host) =>
        {
            task.BeginRenderExecution(isResume: false, resetRetryBudget: true);
            task.CurrentFrame = 12;
            task.FinalizePaused();
            return Task.CompletedTask;
        };

        executionService.ResumeHandler = (task, host, resumeFromFrame) =>
        {
            task.BeginRenderExecution(isResume: true, resetRetryBudget: false);
            task.FinalizeCompleted();
            return Task.CompletedTask;
        };

        await sut.StartQueueAsync();
        await WaitUntilAsync(() => executionService.ResumeCalls == 1);
        await WaitUntilAsync(() => sut.Snapshot.State == QueueExecutionState.Completed);

        Assert.Equal(1, executionService.StartCalls);
        Assert.Equal(1, executionService.ResumeCalls);
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
