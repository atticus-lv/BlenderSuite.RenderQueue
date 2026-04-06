using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Services.Application.Queue;

public sealed class RenderTaskExecutionService : IRenderTaskExecutionService
{
    public Task StartAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost)
    {
        return task.StartRenderAsync(workerHost);
    }

    public Task ResumeAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost, int resumeFromFrame)
    {
        return task.ResumeRenderAsync(workerHost, resumeFromFrame);
    }

    public Task PauseAsync(RenderTaskViewModel task)
    {
        return task.PauseRenderAsync();
    }

    public void Stop(RenderTaskViewModel task)
    {
        task.StopRender();
    }
}
