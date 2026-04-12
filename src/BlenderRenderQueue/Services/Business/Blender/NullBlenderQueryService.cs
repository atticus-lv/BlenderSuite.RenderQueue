using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Blender;

public sealed class NullBlenderQueryService : IBlenderQueryService
{
    public Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesWithTempProcessAsync(
        string blenderPath,
        string blendFilePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult((string.Empty, new Dictionary<string, BlendSceneProperties>()));
    }
}
