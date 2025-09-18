using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public interface IBlenderQueryService
{
	Task<(int frameStart, int frameEnd)> GetSceneFramesAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default);

	Task<string?> GetSceneCameraAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default);

	Task<string?> GetRenderOutputPathAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default);

	Task<string?> GetRenderOutputFormatAsync(BasePythonProcessService process,
		string blendFilePath,
		CancellationToken cancellationToken = default);
} 