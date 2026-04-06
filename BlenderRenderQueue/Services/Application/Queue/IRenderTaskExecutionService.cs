using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.Blender.WorkerHost;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Services.Application.Queue;

public interface IRenderTaskExecutionService
{
    Task StartAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost);
    Task ResumeAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost, int resumeFromFrame);
    Task PauseAsync(RenderTaskViewModel task);
    void Stop(RenderTaskViewModel task);
}
