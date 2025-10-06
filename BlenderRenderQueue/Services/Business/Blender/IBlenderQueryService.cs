using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Blender.BlenderProcess;

namespace BlenderRenderQueue.Services.Business.Blender;

public interface IBlenderQueryService
{
    Task<(string ActiveScene, Dictionary<string, BlendSceneProperties> SceneData)> GetAllFilePropertiesAsync(
        IBlenderProcess process,
        string blendFilePath,
        CancellationToken cancellationToken = default);
} 