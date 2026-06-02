using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using AppLocalizer = BlenderRenderQueue.Localizer.Localizer;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.ViewModels.Logs;

public sealed class GlobalLogEntryViewModel
{
    private readonly Action<Guid> _navigateToTask;

    public GlobalLogEntryViewModel()
        : this(new RenderLogEvent(), string.Empty, _ => { })
    {
    }

    public GlobalLogEntryViewModel(RenderLogEvent logEvent, Action<Guid> navigateToTask)
        : this(logEvent, string.Empty, navigateToTask)
    {
    }

    public GlobalLogEntryViewModel(RenderLogEvent logEvent, string currentSessionId, Action<Guid> navigateToTask)
        : this(logEvent, currentSessionId, navigateToTask, [])
    {
    }

    public GlobalLogEntryViewModel(
        RenderLogEvent logEvent,
        string currentSessionId,
        Action<Guid> navigateToTask,
        IEnumerable<RenderLogEvent> detailEvents)
    {
        Event = logEvent;
        Details = detailEvents
            .OrderBy(logEvent => logEvent.Timestamp)
            .Select(logEvent => new GlobalLogDetailEntryViewModel(logEvent))
            .ToList();
        _navigateToTask = navigateToTask;
        IsCurrentSession = string.Equals(logEvent.SessionId, currentSessionId, StringComparison.Ordinal);
        NavigateToTaskCommand = new RelayCommand(
            () =>
            {
                if (Event.TaskId.HasValue)
                {
                    _navigateToTask(Event.TaskId.Value);
                }
            },
            () => Event.TaskId.HasValue);
    }

    public RenderLogEvent Event { get; }
    public IReadOnlyList<GlobalLogDetailEntryViewModel> Details { get; }
    public IRelayCommand NavigateToTaskCommand { get; }
    public bool IsCurrentSession { get; }
    public bool ShowSessionBadge => !IsCurrentSession;
    public string TimestampText => Event.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string TimeText => Event.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string LevelText => AppLocalizer.Instance[$"RenderLog_Level_{Event.Level}"];
    public string ScopeText => AppLocalizer.Instance[$"RenderLog_Scope_{Event.Scope}"];
    public string AudienceText => RenderLogMetadata.IsDiagnostic(Event)
        ? AppLocalizer.Instance["GlobalLog_Audience_Diagnostic"]
        : AppLocalizer.Instance["GlobalLog_Audience_User"];
    public bool ShowAudienceBadge => RenderLogMetadata.IsDiagnostic(Event);
    public string Message => Event.Message;
    public string SessionText => IsCurrentSession
        ? AppLocalizer.Instance["GlobalLog_CurrentSessionBadge"]
        : AppLocalizer.Instance["GlobalLog_HistoryBadge"];
    public bool HasTask => Event.TaskId.HasValue;
    public string TaskText => !Event.TaskId.HasValue
        ? AppLocalizer.Instance["GlobalLog_NoTask"]
        : !string.IsNullOrWhiteSpace(Event.BlendFilePath)
            ? Path.GetFileName(Event.BlendFilePath)
            : Event.TaskId.Value.ToString("D");
    public bool HasMetadata => Event.Metadata.Count > 0;
    public bool HasDetails => Details.Count > 0;
    public string DetailsHeader => string.Format(AppLocalizer.Instance["GlobalLog_OperationDetails"], Details.Count);
}

public sealed class GlobalLogDetailEntryViewModel(RenderLogEvent logEvent)
{
    public Guid EventId { get; } = logEvent.EventId;
    public string TimeText { get; } = logEvent.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string LevelText { get; } = AppLocalizer.Instance[$"RenderLog_Level_{logEvent.Level}"];
    public string PhaseText { get; } = RenderLogMetadata.GetPhase(logEvent) switch
    {
        RenderLogMetadata.PhaseStart => AppLocalizer.Instance["GlobalLog_Phase_Start"],
        RenderLogMetadata.PhaseDetail => AppLocalizer.Instance["GlobalLog_Phase_Detail"],
        RenderLogMetadata.PhaseSuccess => AppLocalizer.Instance["GlobalLog_Phase_Success"],
        RenderLogMetadata.PhaseError => AppLocalizer.Instance["GlobalLog_Phase_Error"],
        _ => AppLocalizer.Instance["GlobalLog_Phase_Detail"]
    };
    public string Message { get; } = logEvent.Message;
}
