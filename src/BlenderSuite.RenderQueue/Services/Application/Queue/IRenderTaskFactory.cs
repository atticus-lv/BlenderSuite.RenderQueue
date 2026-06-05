using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.ViewModels;

namespace BlenderSuite.RenderQueue.Services.Application.Queue;

public interface IRenderTaskFactory
{
    RenderTaskViewModel Create(
        string blendFilePath,
        int startFrame,
        int endFrame,
        bool animation = true,
        bool overrideFrameRange = false,
        RenderTaskFactoryOptions? options = null);

    RenderTaskViewModel Create(RenderTaskInfo taskInfo, RenderTaskFactoryOptions? options = null);
}

public sealed class RenderTaskFactoryOptions
{
    public int GlobalRenderTimeoutSeconds { get; init; } = 300;
    public int GlobalMaxRetryAttempts { get; init; } = 3;
    public string VideoCodec { get; init; } = "H264";
    public string VideoQuality { get; init; } = "PERC_LOSSLESS";
    public BlenderProcessService? ProcessService { get; init; }
    public bool IsQueueRunning { get; init; }
}
