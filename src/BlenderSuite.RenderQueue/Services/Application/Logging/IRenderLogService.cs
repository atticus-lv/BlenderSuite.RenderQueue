using System;
using System.Collections.Generic;

namespace BlenderSuite.RenderQueue.Services.Application.Logging;

public interface IRenderLogService
{
    string CurrentSessionId { get; }
    event EventHandler<RenderLogEvent>? LogAppended;

    IReadOnlyList<RenderLogEvent> GetEvents(RenderLogProjection? projection = null);
    void Write(RenderLogEvent logEvent);
    void Write(
        RenderLogLevel level,
        RenderLogScope scope,
        string message,
        Guid? taskId = null,
        string? blendFilePath = null,
        string? source = null,
        IReadOnlyDictionary<string, string>? metadata = null);
    void ClearHistory();
    void ClearAll();
}
