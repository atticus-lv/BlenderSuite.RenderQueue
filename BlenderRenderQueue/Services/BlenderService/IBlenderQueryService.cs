using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.BlenderService;

public interface IBlenderQueryService
{
    Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesAsync(
        BasePythonProcessService process,
        string blendFilePath,
        CancellationToken cancellationToken = default);
} 