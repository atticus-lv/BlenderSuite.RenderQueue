using System;
using System.Collections.Generic;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.BlenderService.ServiceOutputParser;

public interface IRenderOutputParser
{
	IReadOnlyList<RenderEvent> ParseLine(string line);
	RenderProgress Current { get; }
	void Reset();
} 