using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.ViewModels;
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
