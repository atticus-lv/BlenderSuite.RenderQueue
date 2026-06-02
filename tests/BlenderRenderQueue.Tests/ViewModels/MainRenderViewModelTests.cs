using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.Services.Business.Persistence;
using BlenderRenderQueue.ViewModels;
using Xunit;

namespace BlenderRenderQueue.Tests.ViewModels;

public sealed class MainRenderViewModelTests
{
    [AvaloniaFact]
    public async Task InitializationWithoutSelectedBlenderShowsMissingBlenderState()
    {
        var logService = TestLogServiceFactory.Create();
        var queueService = CreateQueueService(logService);
        var renderQueue = new RenderQueueViewModel(queueService, logService);
        var settings = new SettingsViewModel(
            new FakeSettingsPersistenceService(new SettingsData()),
            new BlenderValidationService(new FakeBlenderCliInfoService()),
            logService);
        var globalLog = new GlobalLogViewModel(logService);
        var sut = new MainRenderViewModel(settings, renderQueue, globalLog, logService, new BlenderValidationService(new FakeBlenderCliInfoService()));

        try
        {
            await WaitUntilAsync(() => sut.HasBlenderValidationError);
            await DrainUiAsync();

            Assert.False(sut.IsBlenderPathValid);
            Assert.Equal("Blender_SelectExecutable", sut.BlenderValidationMessage);
            Assert.Equal("Blender_PathInvalid", sut.StatusMessage);
            Assert.DoesNotContain(logService.GetEvents(),
                e => e.Source == nameof(MainRenderViewModel) && e.Message.Contains("未选择", StringComparison.Ordinal));
        }
        finally
        {
            sut.Dispose();
            queueService.Dispose();
        }
    }

    private static RenderQueueApplicationService CreateQueueService(IRenderLogService logService)
    {
        return new RenderQueueApplicationService(
            new FakeBlenderWorkerHost(),
            new FakeRenderTaskExecutionService(),
            new FakeDataPersistenceService(),
            logService,
            new RenderTaskFactory(new FakeBlenderQueryService(), logService));
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
            await DrainUiAsync();
        }

        Assert.True(predicate(), "Condition was not met within the allotted timeout.");
    }

    private static Task DrainUiAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    }

    private sealed class FakeSettingsPersistenceService(SettingsData loadedSettings) : ISettingsPersistenceService
    {
        public Task<bool> SaveSettingsAsync(SettingsData settings)
        {
            return Task.FromResult(true);
        }

        public Task<SettingsData> LoadSettingsAsync()
        {
            return Task.FromResult(loadedSettings);
        }
    }

    private sealed class FakeBlenderCliInfoService : IBlenderCliInfoService
    {
        public Task<BlenderVersionInfo> GetVersionInfoAsync(
            string blenderExePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BlenderVersionInfo
            {
                Product = "Blender",
                Version = "Test"
            });
        }
    }
}
