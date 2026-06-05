using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Business.Blender;

namespace BlenderSuite.RenderQueue.Tests;

internal sealed class FakeBlenderQueryService : IBlenderQueryService
{
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
            FrameStart = 1,
            FrameEnd = 1,
            FrameCurrent = 1,
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
