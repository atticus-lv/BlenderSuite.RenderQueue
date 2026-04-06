using System.Collections.Generic;
using System.Linq;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Tests;

internal sealed class FakeLogPersistenceService : ILogPersistenceService
{
    private readonly List<RenderLogEvent> _events = [];

    public string CurrentSessionId { get; } = "test-session";

    public IReadOnlyList<RenderLogEvent> LoadAll()
    {
        return _events.ToList();
    }

    public void Append(RenderLogEvent logEvent)
    {
        _events.Add(logEvent);
    }

    public void ClearHistory()
    {
        _events.RemoveAll(logEvent => logEvent.SessionId != CurrentSessionId);
    }

    public void ClearAll()
    {
        _events.Clear();
    }
}
