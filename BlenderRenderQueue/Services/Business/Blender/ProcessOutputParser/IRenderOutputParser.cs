using System.Collections.Generic;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.BlenderService.ProcessOutputParser;

public interface IRenderOutputParser
{
	IReadOnlyList<RenderEvent> ParseLine(string line);
	RenderProgress Current { get; }
	void Reset();
} 