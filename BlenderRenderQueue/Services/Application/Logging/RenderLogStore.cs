using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Services.Application.Logging;

public sealed class RenderLogStore : IRenderLogStore
{
    private readonly object _syncRoot = new();
    private List<RenderLogEvent> _events = [];

    public IReadOnlyList<RenderLogEvent> GetAll()
    {
        lock (_syncRoot)
        {
            return _events.ToList();
        }
    }

    public IReadOnlyList<RenderLogEvent> Query(RenderLogProjection projection, string currentSessionId)
    {
        lock (_syncRoot)
        {
            return _events
                .Where(e => projection.Matches(e, currentSessionId))
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }

    public void ReplaceAll(IEnumerable<RenderLogEvent> events)
    {
        lock (_syncRoot)
        {
            _events = events.OrderBy(e => e.Timestamp).ToList();
        }
    }

    public void Append(RenderLogEvent logEvent)
    {
        lock (_syncRoot)
        {
            _events.Add(logEvent);
        }
    }

    public void ClearHistory(string currentSessionId)
    {
        lock (_syncRoot)
        {
            _events = _events
                .Where(e => string.Equals(e.SessionId, currentSessionId, System.StringComparison.Ordinal))
                .ToList();
        }
    }

    public void ClearAll()
    {
        lock (_syncRoot)
        {
            _events.Clear();
        }
    }
}
