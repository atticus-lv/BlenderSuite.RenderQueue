using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Views;
using Xunit;

namespace BlenderRenderQueue.Tests.ViewModels;

public sealed class GlobalLogViewModelTests
{
    [AvaloniaFact]
    public async Task MatchingAppend_IsInsertedIncrementally()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "Older event");

        using var sut = new GlobalLogViewModel(logService);

        logService.Write(RenderLogLevel.Error, RenderLogScope.Task, "Newest event", Guid.NewGuid(), "/tmp/example.blend");
        await DrainUiAsync();

        Assert.Equal(2, sut.Entries.Count);
        Assert.Equal("Newest event", sut.Entries[0].Message);
        Assert.Contains(sut.TaskOptions, option => option.TaskId.HasValue);
    }

    [AvaloniaFact]
    public async Task FilteredOutAppend_DoesNotChangeVisibleEntries()
    {
        var logService = TestLogServiceFactory.Create();
        using var sut = new GlobalLogViewModel(logService);

        sut.SelectedLevel = "GlobalLog_Filter_ErrorsOnly";
        await WaitForDebounceAsync();

        logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "Info event");
        await DrainUiAsync();

        Assert.Empty(sut.Entries);
    }

    [AvaloniaFact]
    public async Task DefaultView_HidesDiagnosticEntries()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(RenderLogLevel.Info, RenderLogScope.System, "User event");
        logService.Write(
            RenderLogLevel.Info,
            RenderLogScope.System,
            "Diagnostic event",
            metadata: RenderLogMetadata.Diagnostic());

        using var sut = new GlobalLogViewModel(logService);
        await DrainUiAsync();

        Assert.Single(sut.Entries);
        Assert.Equal("User event", sut.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task DiagnosticView_ShowsDiagnosticEntries()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(
            RenderLogLevel.Info,
            RenderLogScope.System,
            "Diagnostic event",
            metadata: RenderLogMetadata.Diagnostic());

        using var sut = new GlobalLogViewModel(logService);

        sut.SelectedAudience = "GlobalLog_Filter_DiagnosticLogs";
        await WaitForDebounceAsync();

        Assert.Single(sut.Entries);
        Assert.Equal("Diagnostic event", sut.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task DebugOnlyLevel_DoesNotMixInfoEntries()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(RenderLogLevel.Info, RenderLogScope.System, "Info event");
        logService.Write(RenderLogLevel.Debug, RenderLogScope.System, "Debug event");

        using var sut = new GlobalLogViewModel(logService);

        sut.SelectedAudience = "GlobalLog_Filter_DiagnosticLogs";
        sut.SelectedLevel = "GlobalLog_Filter_DebugOnly";
        await WaitForDebounceAsync();

        Assert.Single(sut.Entries);
        Assert.Equal("Debug event", sut.Entries[0].Message);
    }

    [AvaloniaFact]
    public async Task OperationEntries_AreGroupedWithDetails()
    {
        var logService = TestLogServiceFactory.Create();
        var operation = logService.BeginOperation(
            RenderLogScope.Recovery,
            "LoadSettings",
            "Test",
            "Start loading settings");
        operation.Detail("Loading settings from /tmp/settings.json");
        operation.Complete("Settings loaded");

        using var sut = new GlobalLogViewModel(logService);

        Assert.Single(sut.Entries);
        Assert.Equal("Settings loaded", sut.Entries[0].Message);
        Assert.Empty(sut.Entries[0].Details);

        sut.SelectedAudience = "GlobalLog_Filter_AllAudiences";
        await WaitForDebounceAsync();

        Assert.Single(sut.Entries);
        Assert.Equal("Settings loaded", sut.Entries[0].Message);
        Assert.Equal(2, sut.Entries[0].Details.Count);
    }

    [AvaloniaFact]
    public void HistoricalSessionEntries_AreCollapsedInAllSessionsView()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(new RenderLogEvent
        {
            SessionId = "older-session",
            Level = RenderLogLevel.Info,
            Scope = RenderLogScope.Queue,
            Message = "Old event 1"
        });
        logService.Write(new RenderLogEvent
        {
            SessionId = "older-session",
            Level = RenderLogLevel.Warning,
            Scope = RenderLogScope.Queue,
            Message = "Old event 2"
        });
        logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "Current event");

        using var sut = new GlobalLogViewModel(logService);

        Assert.Equal(2, sut.Entries.Count);
        var historyEntry = sut.Entries.Single(entry => entry.ShowSessionBadge);
        Assert.Contains("2", historyEntry.Message, StringComparison.Ordinal);
        Assert.Equal(2, historyEntry.Details.Count);
        Assert.Contains(historyEntry.Details, detail => detail.Message == "Old event 1");
        Assert.Contains(historyEntry.Details, detail => detail.Message == "Old event 2");
    }

    [AvaloniaFact]
    public void ClearHistory_IsDisabledWhenOnlyCurrentSessionEntriesExist()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(new RenderLogEvent
        {
            SessionId = logService.CurrentSessionId,
            Level = RenderLogLevel.Info,
            Scope = RenderLogScope.Queue,
            Message = "Current event"
        });

        using var sut = new GlobalLogViewModel(logService);

        Assert.False(sut.HasHistoricalEntries);

        sut.ClearHistoryCommand.Execute(null);

        Assert.Single(sut.Entries);
        Assert.False(sut.HasHistoricalEntries);
    }

    [AvaloniaFact]
    public async Task ClearHistoryCommand_WithBoundView_RemovesHistoricalEntriesWithoutBreakingBindings()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(new RenderLogEvent
        {
            SessionId = "older-session",
            Level = RenderLogLevel.Warning,
            Scope = RenderLogScope.Queue,
            Message = "Historical event"
        });
        logService.Write(new RenderLogEvent
        {
            SessionId = logService.CurrentSessionId,
            Level = RenderLogLevel.Info,
            Scope = RenderLogScope.Queue,
            Message = "Current event"
        });

        using var sut = new GlobalLogViewModel(logService);
        var view = new GlobalLogView
        {
            DataContext = sut
        };

        // Force template application/binding materialization.
        var host = new Window
        {
            Content = view
        };
        host.Show();
        await DrainUiAsync();

        sut.SelectedSession = "GlobalLog_Filter_HistoryOnly";
        await WaitForDebounceAsync();
        Assert.Single(sut.Entries);
        Assert.True(sut.HasHistoricalEntries);

        sut.ClearHistoryCommand.Execute(null);
        await DrainUiAsync();

        Assert.Empty(sut.Entries);
        Assert.False(sut.HasHistoricalEntries);
        Assert.Single(sut.TaskOptions);
        Assert.Null(sut.TaskOptions[0].TaskId);
        Assert.Equal(sut.TaskOptions[0], sut.SelectedTask);

        host.Close();
    }

    [AvaloniaFact]
    public void ClearAllCommand_RemovesCurrentAndHistoricalEntries()
    {
        var logService = TestLogServiceFactory.Create();
        logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "Current event");
        logService.Write(new RenderLogEvent
        {
            SessionId = "older-session",
            Level = RenderLogLevel.Warning,
            Scope = RenderLogScope.Queue,
            Message = "Historical event"
        });

        using var sut = new GlobalLogViewModel(logService);

        sut.ClearAllCommand.Execute(null);

        Assert.Empty(sut.Entries);
        Assert.Empty(logService.GetEvents());
        Assert.False(sut.HasHistoricalEntries);
        Assert.Single(sut.TaskOptions);
    }

    [AvaloniaFact]
    public async Task DuplicateEventId_IsDisplayedOnlyOnce()
    {
        var logService = TestLogServiceFactory.Create();
        var logEvent = new RenderLogEvent
        {
            EventId = Guid.NewGuid(),
            Level = RenderLogLevel.Info,
            Scope = RenderLogScope.Queue,
            Message = "Same event"
        };

        using var sut = new GlobalLogViewModel(logService);

        logService.Write(logEvent);
        logService.Write(logEvent);
        await DrainUiAsync();

        Assert.Single(sut.Entries);
        Assert.Equal(logEvent.EventId, sut.Entries[0].Event.EventId);
    }

    [AvaloniaFact]
    public async Task ClearAllCommand_DropsQueuedAppendCallbacks()
    {
        var logService = TestLogServiceFactory.Create();
        using var sut = new GlobalLogViewModel(logService);

        logService.Write(RenderLogLevel.Info, RenderLogScope.Queue, "Queued event");
        sut.ClearAllCommand.Execute(null);
        await DrainUiAsync();

        Assert.Empty(sut.Entries);
        Assert.Empty(logService.GetEvents());
    }

    private static async Task WaitForDebounceAsync()
    {
        await Task.Delay(250);
        await DrainUiAsync();
    }

    private static Task DrainUiAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    }
}
