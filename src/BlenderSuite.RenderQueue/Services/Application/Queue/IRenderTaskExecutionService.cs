using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Services.Business.Blender.WorkerHost;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Services.Application.Queue;

public interface IRenderTaskExecutionService
{
    Task StartAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost);
    Task ResumeAsync(RenderTaskViewModel task, IBlenderWorkerHost workerHost, int resumeFromFrame);
    Task PauseAsync(RenderTaskViewModel task);
    void Stop(RenderTaskViewModel task);
}
