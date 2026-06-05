using System.Collections.Generic;

namespace BlenderSuite.RenderQueue.Services.Application.Logging;

public interface IRenderLogStore
{
    IReadOnlyList<RenderLogEvent> GetAll();
    IReadOnlyList<RenderLogEvent> Query(RenderLogProjection projection, string currentSessionId);
    void ReplaceAll(IEnumerable<RenderLogEvent> events);
    void Append(RenderLogEvent logEvent);
    void ClearHistory(string currentSessionId);
    void ClearAll();
}
