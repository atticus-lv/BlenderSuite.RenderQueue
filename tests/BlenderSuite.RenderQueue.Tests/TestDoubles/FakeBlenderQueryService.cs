using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Business.Blender;

namespace BlenderSuite.RenderQueue.Tests;

internal sealed class FakeBlenderQueryService : IBlenderQueryService
{
    private readonly int _frameStart;
    private readonly int _frameEnd;

    public FakeBlenderQueryService(int frameStart = 1, int frameEnd = 1)
    {
        _frameStart = frameStart;
        _frameEnd = frameEnd;
    }

    public Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesWithTempProcessAsync(
        string blenderPath,
        string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        var scene = new BlendSceneProperties
        {
            FilePath = blendFilePath,
            SceneName = "Scene",
            IsDefaultScene = true,
            FrameStart = _frameStart,
            FrameEnd = _frameEnd,
            FrameCurrent = _frameStart,
            FramePath = "/tmp/frame_####.png"
        };

        return Task.FromResult((
            "Scene",
            new Dictionary<string, BlendSceneProperties>
            {
                ["Scene"] = scene
            }));
    }
}
