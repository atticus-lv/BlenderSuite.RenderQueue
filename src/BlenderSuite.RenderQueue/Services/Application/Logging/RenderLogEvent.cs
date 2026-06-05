using System;
using System.Collections.Generic;

namespace BlenderSuite.RenderQueue.Services.Application.Logging;

public sealed class RenderLogEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public RenderLogLevel Level { get; init; } = RenderLogLevel.Info;
    public RenderLogScope Scope { get; init; } = RenderLogScope.System;
    public string Message { get; init; } = string.Empty;
    public Guid? TaskId { get; init; }
    public string BlendFilePath { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
