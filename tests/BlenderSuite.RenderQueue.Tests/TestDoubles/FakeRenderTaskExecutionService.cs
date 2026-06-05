using System;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application.Queue;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Tests;

internal sealed class FakeRenderTaskExecutionService : IRenderTaskExecutionService
{
    public int StartCalls { get; private set; }
    public int ResumeCalls { get; private set; }
    public int PauseCalls { get; private set; }
    public int StopCalls { get; private set; }
    public Func<RenderTaskViewModel, IBlenderWorkerHost, Task>? StartHandler { get; set; }
    public Func<RenderTaskViewModel, IBlenderWorkerHost, int, Task>? ResumeHandler { get; set; }
    public Func<RenderTaskViewModel, Task>? PauseHandler { get; set; }
    public Action<RenderTaskViewModel>? StopHandler { get; set; }

    public Task StartAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost)
    {
        StartCalls++;
        if (StartHandler != null)
        {
            return StartHandler(task, workerHost);
        }

        task.BeginRenderExecution(isResume: false, resetRetryBudget: true);
        task.FinalizeCompleted();
        return Task.CompletedTask;
    }

    public Task ResumeAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost, int resumeFromFrame)
    {
        ResumeCalls++;
        if (ResumeHandler != null)
        {
            return ResumeHandler(task, workerHost, resumeFromFrame);
        }

        task.BeginRenderExecution(isResume: true, resetRetryBudget: false);
        task.FinalizeCompleted();
        return Task.CompletedTask;
    }

    public Task PauseAsync(RenderTaskViewModel task)
    {
        PauseCalls++;
        if (PauseHandler != null)
        {
            return PauseHandler(task);
        }

        task.FinalizePaused();
        return Task.CompletedTask;
    }

    public void Stop(RenderTaskViewModel task)
    {
        StopCalls++;
        if (StopHandler != null)
        {
            StopHandler(task);
            return;
        }

        task.FinalizeStopped();
    }
}
