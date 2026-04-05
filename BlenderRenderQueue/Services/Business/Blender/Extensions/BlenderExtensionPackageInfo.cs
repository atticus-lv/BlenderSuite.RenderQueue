namespace BlenderRenderQueue.Services.Business.Blender.Extensions;

public sealed class BlenderExtensionPackageInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string PackagePath { get; init; }
    public required string ManifestSource { get; init; }
}
