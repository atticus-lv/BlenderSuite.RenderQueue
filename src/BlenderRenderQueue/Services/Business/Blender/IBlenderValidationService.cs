using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.Business.Blender;

public interface IBlenderValidationService
{
    BlenderValidationRequest BeginValidation(string? path, string channel = BlenderValidationService.DefaultChannel);
    BlenderValidationResult? ValidatePreconditions(BlenderValidationRequest request);
    Task<BlenderValidationResult> ValidateAsync(BlenderValidationRequest request, CancellationToken cancellationToken = default);
    Task<BlenderValidationResult> ValidatePathAsync(string? path, CancellationToken cancellationToken = default);
    bool IsCurrent(BlenderValidationRequest request);
    void CancelCurrent(string channel = BlenderValidationService.DefaultChannel);
}
