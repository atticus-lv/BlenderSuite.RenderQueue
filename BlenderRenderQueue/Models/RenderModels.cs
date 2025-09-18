using System;

namespace BlenderRenderQueue.Models;

public enum RenderEngine
{
	Unknown,
	Cycles,
	Eevee,
	Workbench
}

public sealed record RenderProgress
{
	public int CurrentFrame { get; init; }
	public int? StartFrame { get; init; }
	public int? EndFrame { get; init; }
	public int? SampleCurrent { get; init; }
	public int? SampleTotal { get; init; }
	public RenderEngine Engine { get; init; }
	public double? MemoryMB { get; init; }
	public string? Scene { get; init; }
	public string? ViewLayer { get; init; }
	public string? SavedPath { get; init; }
	public TimeSpan? Elapsed { get; init; }
}

public abstract record RenderEvent;

public record RenderSessionStarted(bool IsAnimation, int? StartFrame, int? EndFrame) : RenderEvent;
public record RenderStarted(int Frame, string? Scene, string? ViewLayer, RenderEngine Engine) : RenderEvent;
public record RenderProgressEvent(RenderProgress Progress) : RenderEvent;
public record RenderSaved(string Path, int Frame) : RenderEvent;
public record RenderCompletedFrame(int Frame, TimeSpan Time, TimeSpan? Saving) : RenderEvent;
public record RenderCompletedAll(TimeSpan? TotalTime = null) : RenderEvent;
public record RenderOutput(string Line) : RenderEvent;
public record RenderError(string Message) : RenderEvent; 