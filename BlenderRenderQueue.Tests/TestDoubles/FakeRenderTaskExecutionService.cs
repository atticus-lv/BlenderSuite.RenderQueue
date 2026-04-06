using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Queue;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Tests;

internal sealed class FakeRenderTaskExecutionService : IRenderTaskExecutionService
{
    public int StartCalls { get; private set; }
    public int ResumeCalls { get; private set; }
    public int PauseCalls { get; private set; }
    public int StopCalls { get; private set; }

    public Task StartAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost)
    {
        StartCalls++;
        task.BeginRenderExecution(isResume: false, resetRetryBudget: true);
        task.FinalizeCompleted();
        return Task.CompletedTask;
    }

    public Task ResumeAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost, int resumeFromFrame)
    {
        ResumeCalls++;
        task.BeginRenderExecution(isResume: true, resetRetryBudget: false);
        task.FinalizeCompleted();
        return Task.CompletedTask;
    }

    public Task PauseAsync(RenderTaskViewModel task)
    {
        PauseCalls++;
        task.FinalizePaused();
        return Task.CompletedTask;
    }

    public void Stop(RenderTaskViewModel task)
    {
        StopCalls++;
        task.FinalizeStopped();
    }
}
