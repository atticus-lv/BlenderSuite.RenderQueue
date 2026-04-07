using System;
using System.Collections.Generic;
using System.Linq;

namespace BlenderRenderQueue.Services.Application.Logging;

public sealed class RenderLogProjection
{
    public Guid? TaskId { get; init; }
    public string? SessionId { get; init; }
    public bool CurrentSessionOnly { get; init; }
    public bool HistoricalOnly { get; init; }
    public bool IncludeDebug { get; init; } = true;
    public bool IncludeRaw { get; init; } = true;
    public IReadOnlyCollection<RenderLogLevel>? Levels { get; init; }
    public IReadOnlyCollection<RenderLogScope>? Scopes { get; init; }
    public string SearchText { get; init; } = string.Empty;

    public bool Matches(RenderLogEvent logEvent, string currentSessionId)
    {
        if (TaskId.HasValue && logEvent.TaskId != TaskId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SessionId) &&
            !string.Equals(logEvent.SessionId, SessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (CurrentSessionOnly &&
            !string.Equals(logEvent.SessionId, currentSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (HistoricalOnly &&
            string.Equals(logEvent.SessionId, currentSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!IncludeDebug && logEvent.Level == RenderLogLevel.Debug)
        {
            return false;
        }

        if (Levels is { Count: > 0 } && !Levels.Contains(logEvent.Level))
        {
            return false;
        }

        if (Scopes is { Count: > 0 } && !Scopes.Contains(logEvent.Scope))
        {
            return false;
        }

        var isRaw = logEvent.Metadata.TryGetValue("kind", out var kind) &&
                    string.Equals(kind, "raw", StringComparison.OrdinalIgnoreCase);
        if (!IncludeRaw && isRaw)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        if (logEvent.Message.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(logEvent.BlendFilePath) &&
            logEvent.BlendFilePath.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return logEvent.Metadata.Values.Any(value =>
            value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }
}
