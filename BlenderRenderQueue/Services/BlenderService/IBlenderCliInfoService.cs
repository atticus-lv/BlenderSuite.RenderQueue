using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public interface IBlenderCliInfoService
{
	Task<BlenderVersionInfo> GetVersionInfoAsync(string blenderExePath, CancellationToken cancellationToken = default);
} 