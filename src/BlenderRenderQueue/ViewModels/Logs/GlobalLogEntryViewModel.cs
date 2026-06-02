using System;
using System.IO;
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
    {
        Event = logEvent;
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
    public IRelayCommand NavigateToTaskCommand { get; }
    public bool IsCurrentSession { get; }
    public bool ShowSessionBadge => !IsCurrentSession;
    public string TimestampText => Event.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string TimeText => Event.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string LevelText => AppLocalizer.Instance[$"RenderLog_Level_{Event.Level}"];
    public string ScopeText => AppLocalizer.Instance[$"RenderLog_Scope_{Event.Scope}"];
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
}
