using BlenderSuite.RenderQueue.Services.Application.Logging;

namespace BlenderSuite.RenderQueue.Tests;

internal static class TestLogServiceFactory
{
    public static IRenderLogService Create()
    {
        return new RenderLogService(new RenderLogStore(), new FakeLogPersistenceService());
    }
}
