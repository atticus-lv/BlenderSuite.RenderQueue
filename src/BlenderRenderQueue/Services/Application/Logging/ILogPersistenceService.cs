using System.Collections.Generic;

namespace BlenderRenderQueue.Services.Application.Logging;

public interface ILogPersistenceService
{
    string CurrentSessionId { get; }
    IReadOnlyList<RenderLogEvent> LoadAll();
    void Append(RenderLogEvent logEvent);
    void ClearHistory();
    void ClearAll();
}
