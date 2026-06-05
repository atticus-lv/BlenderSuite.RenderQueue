using BlenderSuite.RenderQueue.Services.Application.Queue;
using Xunit;

namespace BlenderSuite.RenderQueue.Tests.Services.Application.Queue;

public sealed class RenderTaskFactoryTests
{
    [Fact]
    public void Create_InitializesTaskWithQueryAndLogDependencies()
    {
        var logService = TestLogServiceFactory.Create();
        var factory = new RenderTaskFactory(new FakeBlenderQueryService(), logService);

        var task = factory.Create(
            "/tmp/example.blend",
            1,
            2,
            options: new RenderTaskFactoryOptions
            {
                GlobalRenderTimeoutSeconds = 42,
                GlobalMaxRetryAttempts = 7,
                VideoCodec = "AV1",
                VideoQuality = "HIGH",
                IsQueueRunning = true
            });

        Assert.Equal(42, task.GetGlobalRenderTimeoutSeconds());
        Assert.Equal(7, task.GetGlobalMaxRetryAttempts());
        Assert.False(task.CanRefresh);

        task.Dispose();
    }
}
