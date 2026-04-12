using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Blender;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Services.Application.Queue;

public sealed class RenderTaskFactory(
    IBlenderQueryService queryService,
    IRenderLogService logService) : IRenderTaskFactory
{
    private readonly IBlenderQueryService _queryService = queryService;
    private readonly IRenderLogService _logService = logService;

    public RenderTaskViewModel Create(
        string blendFilePath,
        int startFrame,
        int endFrame,
        bool animation = true,
        bool overrideFrameRange = false,
        RenderTaskFactoryOptions? options = null)
    {
        var task = new RenderTaskViewModel(
            new BlendScenePropertiesViewModel(_queryService),
            blendFilePath,
            startFrame,
            endFrame,
            animation,
            overrideFrameRange);
        return Initialize(task, options);
    }

    public RenderTaskViewModel Create(RenderTaskInfo taskInfo, RenderTaskFactoryOptions? options = null)
    {
        var task = new RenderTaskViewModel(new BlendScenePropertiesViewModel(_queryService), taskInfo);
        return Initialize(task, options);
    }

    private RenderTaskViewModel Initialize(RenderTaskViewModel task, RenderTaskFactoryOptions? options)
    {
        task.SetGlobalRenderTimeout(options?.GlobalRenderTimeoutSeconds ?? 300);
        task.SetGlobalMaxRetryAttempts(options?.GlobalMaxRetryAttempts ?? 3);
        task.SetVideoCodec(options?.VideoCodec ?? "H264");
        task.SetVideoQuality(options?.VideoQuality ?? "PERC_LOSSLESS");
        task.SetProcessService(options?.ProcessService);
        task.AttachLogService(_logService);
        task.SetQueueRunningState(options?.IsQueueRunning ?? false);
        return task;
    }
}
