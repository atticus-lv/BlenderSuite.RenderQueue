using System;
using System.Linq;
using AppLocalizer = BlenderRenderQueue.Localizer.Localizer;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.ViewModels.Logs;

public sealed class TaskLogEntryViewModel
{
    public TaskLogEntryViewModel(RenderLogEvent logEvent)
    {
        Event = logEvent;
    }

    public RenderLogEvent Event { get; }
    public string TimestampText => Event.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string TimeText => Event.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string LevelText => AppLocalizer.Instance[$"RenderLog_Level_{Event.Level}"];
    public string ScopeText => AppLocalizer.Instance[$"RenderLog_Scope_{Event.Scope}"];
    public string Message => Event.Message;
    public bool HasMetadata => Event.Metadata.Count > 0;
    public string MetadataText => string.Join(
        Environment.NewLine,
        Event.Metadata.Select(pair => $"{pair.Key}: {pair.Value}"));
}
