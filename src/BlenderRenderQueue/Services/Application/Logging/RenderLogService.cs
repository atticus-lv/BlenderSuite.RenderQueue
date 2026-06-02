using System;
using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Services.Application.Logging;

public sealed class RenderLogService : IRenderLogService
{
    private readonly IRenderLogStore _store;
    private readonly ILogPersistenceService _persistenceService;

    public RenderLogService(IRenderLogStore store, ILogPersistenceService persistenceService)
    {
        _store = store;
        _persistenceService = persistenceService;
        _store.ReplaceAll(_persistenceService.LoadAll());
    }

    public string CurrentSessionId => _persistenceService.CurrentSessionId;

    public event EventHandler<RenderLogEvent>? LogAppended;

    public IReadOnlyList<RenderLogEvent> GetEvents(RenderLogProjection? projection = null)
    {
        if (projection == null)
        {
            return _store.GetAll().OrderByDescending(e => e.Timestamp).ToList();
        }

        return _store.Query(projection, CurrentSessionId);
    }

    public void Write(RenderLogEvent logEvent)
    {
        var metadata = NormalizeMetadata(logEvent);
        var normalized = new RenderLogEvent
        {
            EventId = logEvent.EventId == Guid.Empty ? Guid.NewGuid() : logEvent.EventId,
            Timestamp = logEvent.Timestamp == default ? DateTimeOffset.UtcNow : logEvent.Timestamp,
            Level = logEvent.Level,
            Scope = logEvent.Scope,
            Message = logEvent.Message,
            TaskId = logEvent.TaskId,
            BlendFilePath = logEvent.BlendFilePath ?? string.Empty,
            SessionId = string.IsNullOrWhiteSpace(logEvent.SessionId) ? CurrentSessionId : logEvent.SessionId,
            Source = logEvent.Source ?? string.Empty,
            Metadata = metadata
        };

        _store.Append(normalized);
        _persistenceService.Append(normalized);
        LogAppended?.Invoke(this, normalized);
    }

    public void Write(
        RenderLogLevel level,
        RenderLogScope scope,
        string message,
        Guid? taskId = null,
        string? blendFilePath = null,
        string? source = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Write(new RenderLogEvent
        {
            Level = level,
            Scope = scope,
            Message = message,
            TaskId = taskId,
            BlendFilePath = blendFilePath ?? string.Empty,
            Source = source ?? string.Empty,
            Metadata = metadata ?? new Dictionary<string, string>()
        });
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(RenderLogEvent logEvent)
    {
        var metadata = logEvent.Metadata == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(logEvent.Metadata);
        if (metadata.ContainsKey(RenderLogMetadata.AudienceKey))
        {
            return metadata;
        }

        var isRaw = metadata.TryGetValue(RenderLogMetadata.KindKey, out var kind) &&
                    string.Equals(kind, RenderLogMetadata.KindRaw, StringComparison.OrdinalIgnoreCase);
        metadata[RenderLogMetadata.AudienceKey] =
            logEvent.Level == RenderLogLevel.Debug || isRaw
                ? RenderLogMetadata.AudienceDiagnostic
                : RenderLogMetadata.AudienceUser;
        return metadata;
    }

    public void ClearHistory()
    {
        _store.ClearHistory(CurrentSessionId);
        _persistenceService.ClearHistory();
    }

    public void ClearAll()
    {
        _store.ClearAll();
        _persistenceService.ClearAll();
    }
}
