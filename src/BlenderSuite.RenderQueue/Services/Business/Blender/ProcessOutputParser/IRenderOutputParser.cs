using System.Collections.Generic;
using BlenderSuite.RenderQueue.Models;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser;

public interface IRenderOutputParser
{
	IReadOnlyList<RenderEvent> ParseLine(string line);
	RenderProgress Current { get; }
	void Reset();
} 