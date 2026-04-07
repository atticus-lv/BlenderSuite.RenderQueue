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

        sut.ClearHistoryCommand.Execute(null);
        await DrainUiAsync();

        Assert.Empty(sut.Entries);
        Assert.Single(sut.TaskOptions);
        Assert.Null(sut.TaskOptions[0].TaskId);
        Assert.Equal(sut.TaskOptions[0], sut.SelectedTask);

        host.Close();
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
