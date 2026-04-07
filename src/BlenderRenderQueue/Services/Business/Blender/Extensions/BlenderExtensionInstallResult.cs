namespace BlenderRenderQueue.Services.Business.Blender.Extensions;

public enum BlenderExtensionInstallOutcome
{
    Skipped,
    AlreadyUpToDate,
    Installed,
    Updated,
    Failed
}

public sealed class BlenderExtensionInstallResult
{
    public BlenderExtensionInstallOutcome Outcome { get; init; }
    public string BlenderExecutablePath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? PreviousVersion { get; init; }
    public string? InstalledVersion { get; init; }
    public BlenderExtensionPackageInfo? PackageInfo { get; init; }

    public bool IsSuccess => Outcome != BlenderExtensionInstallOutcome.Failed;
}
