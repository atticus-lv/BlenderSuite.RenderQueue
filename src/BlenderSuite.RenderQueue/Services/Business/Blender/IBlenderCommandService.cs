using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Services.Business.Blender.BlenderProcess;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

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