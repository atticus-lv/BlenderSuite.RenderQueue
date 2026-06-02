using System;
using System.Collections.Generic;

namespace BlenderRenderQueue.Services.Application.Logging;

public static class RenderLogMetadata
{
    public const string AudienceKey = "audience";
    public const string KindKey = "kind";
    public const string AudienceUser = "user";
    public const string AudienceTimeline = "timeline";
    public const string AudienceDiagnostic = "diagnostic";
    public const string AudienceDebug = "debug";
    public const string KindRaw = "raw";
    public const string OperationIdKey = "operation_id";
    public const string OperationNameKey = "operation_name";
    public const string PhaseKey = "phase";
    public const string DurationMsKey = "duration_ms";
    public const string PhaseStart = "start";
    public const string PhaseDetail = "detail";
    public const string PhaseSuccess = "success";
    public const string PhaseError = "error";

    public static IReadOnlyDictionary<string, string> WithAudience(
        IReadOnlyDictionary<string, string>? metadata,
        string audience)
    {
        var merged = metadata == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata);
        merged[AudienceKey] = audience;
        return merged;
    }

    public static IReadOnlyDictionary<string, string> Diagnostic(IReadOnlyDictionary<string, string>? metadata = null)
    {
        return WithAudience(metadata, AudienceDiagnostic);
    }

    public static IReadOnlyDictionary<string, string> ForOperation(
        IReadOnlyDictionary<string, string>? metadata,
        Guid operationId,
        string operationName,
        string phase,
        string audience,
        long? durationMs = null)
    {
        var merged = metadata == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata);
        merged[AudienceKey] = audience;
        merged[OperationIdKey] = operationId.ToString("D");
        merged[OperationNameKey] = operationName;
        merged[PhaseKey] = phase;
        if (durationMs.HasValue)
        {
            merged[DurationMsKey] = durationMs.Value.ToString();
        }

        return merged;
    }

    public static bool TryGetOperationId(RenderLogEvent logEvent, out string operationId)
    {
        return logEvent.Metadata.TryGetValue(OperationIdKey, out operationId!) &&
               !string.IsNullOrWhiteSpace(operationId);
    }

    public static string GetPhase(RenderLogEvent logEvent)
    {
        return logEvent.Metadata.TryGetValue(PhaseKey, out var phase)
            ? phase
            : string.Empty;
    }

    public static string GetAudience(RenderLogEvent logEvent)
    {
        return logEvent.Metadata.TryGetValue(AudienceKey, out var audience)
            ? audience
            : AudienceUser;
    }

    public static bool IsDiagnostic(RenderLogEvent logEvent)
    {
        var audience = GetAudience(logEvent);
        if (string.Equals(audience, AudienceDiagnostic, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(audience, AudienceDebug, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return logEvent.Metadata.TryGetValue(KindKey, out var kind) &&
               string.Equals(kind, KindRaw, System.StringComparison.OrdinalIgnoreCase);
    }
}
