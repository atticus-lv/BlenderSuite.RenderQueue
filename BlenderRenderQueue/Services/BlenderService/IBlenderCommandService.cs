using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public interface IBlenderCommandService
{
	Task StartRenderAsync(BasePythonProcessService process,
		string blendFilePath,
		bool animation,
		int? startFrame = null,
		int? endFrame = null,
		CancellationToken cancellationToken = default);
} 