using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.BlenderService.BlenderProcess;

namespace BlenderRenderQueue.Services.Business.BlenderService;

public interface IBlenderCommandService
{
	Task StartRenderAsync(IBlenderProcess process,
		string blendFilePath,
		bool animation,
		int? startFrame = null,
		int? endFrame = null,
		string? sceneName = null,
		CancellationToken cancellationToken = default);
} 