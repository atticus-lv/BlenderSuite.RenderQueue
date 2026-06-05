using System.Threading;
using System.Threading.Tasks;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

public interface IBlenderCliInfoService
{
	Task<BlenderVersionInfo> GetVersionInfoAsync(string blenderExePath, CancellationToken cancellationToken = default);
} 