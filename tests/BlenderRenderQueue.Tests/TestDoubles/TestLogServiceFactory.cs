using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Tests;

internal static class TestLogServiceFactory
{
    public static IRenderLogService Create()
    {
        return new RenderLogService(new RenderLogStore(), new FakeLogPersistenceService());
    }
}
