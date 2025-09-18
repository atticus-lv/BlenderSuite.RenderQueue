using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public interface IBlenderCommandService
{
	Task StartRenderAsync(BasePythonProcessService process,
		string blendFilePath,
		int startFrame,
		int endFrame,
		bool animation,
		CancellationToken cancellationToken = default);
} 