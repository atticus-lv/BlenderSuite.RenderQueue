using System.Collections.Generic;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser;

public interface IRenderOutputParser
{
	IReadOnlyList<RenderEvent> ParseLine(string line);
	RenderProgress Current { get; }
	void Reset();
} 