using System;
using System.Text.RegularExpressions;

namespace BlenderRenderQueue.Services.BlenderService.ServiceOutputParser;

public class RenderProgress
{
    public string MemoryUsage { get; set; } = "0M";
    public string RenderTime { get; set; } = "00:00.00";
    public int CurrentSamples { get; set; }
    public int TotalSamples { get; set; }
    public double Progress { get; set; }
}