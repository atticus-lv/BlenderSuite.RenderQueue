using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

public interface IBlenderQueryService
{
    Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesWithTempProcessAsync(
        string blenderPath,
        string blendFilePath,
        CancellationToken cancellationToken = default);
} 