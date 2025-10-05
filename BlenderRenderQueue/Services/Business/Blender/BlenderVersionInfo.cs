using System;

namespace BlenderRenderQueue.Services.Business.BlenderService;

public sealed class BlenderVersionInfo
{
	public string Product { get; set; } = string.Empty; // e.g. Blender
	public string Version { get; set; } = string.Empty; // e.g. 5.0.0 Alpha
	public DateTime? BuildDate { get; set; }
	public string? BuildTime { get; set; }
	public DateTime? CommitDate { get; set; }
	public string? CommitTime { get; set; }
	public string? Hash { get; set; }
	public string? Branch { get; set; }
	public string? Platform { get; set; }
	public string? Type { get; set; }
} 