using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.BlenderService.BlenderProcess;

namespace BlenderRenderQueue.Services.Business.BlenderService;

public interface IBlenderQueryService
{
    Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesAsync(
        IBlenderProcess process,
        string blendFilePath,
        CancellationToken cancellationToken = default);
} 