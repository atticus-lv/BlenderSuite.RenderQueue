using System.Collections.Generic;

namespace BlenderSuite.RenderQueue.Services.Application.Logging;

public interface ILogPersistenceService
{
    string CurrentSessionId { get; }
    IReadOnlyList<RenderLogEvent> LoadAll();
    void Append(RenderLogEvent logEvent);
    void ClearHistory();
    void ClearAll();
}
