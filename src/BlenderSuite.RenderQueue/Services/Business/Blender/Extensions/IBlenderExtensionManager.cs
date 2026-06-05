using System.Threading;
using System.Threading.Tasks;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.Extensions;

public interface IBlenderExtensionManager
{
    Task<BlenderExtensionInstallResult> EnsureInstalledAsync(
        string blenderExecutablePath,
        CancellationToken cancellationToken = default);
}
