using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.BlenderService.ProcessOutputParser.Core;

namespace BlenderRenderQueue.Services.Business.BlenderService.ProcessOutputParser.Info;

/// <summary>
/// 渲染进度解析器
/// </summary>
public class RenderProgressParser : IInfoParser<RenderProgress>
{
    // Cycles 采样行: Mem: 2983M | Sample 1/16
    private static readonly Regex RxCyclesSample = new(@"Mem:\s+(?<mem>\d+)M\s+\|\s+Sample\s+(?<cur>\d+)\/(?<tot>\d+)", RegexOptions.Compiled);
    
    // Eevee 采样行: Rendering 25 / 64 samples
    private static readonly Regex RxEeveeSample = new(@"Rendering\s+(?<cur>\d+)\s*\/\s*(?<tot>\d+)\s+samples", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // 保存: Saved: 'C:\tmp\0001.png'
    private static readonly Regex RxSaved = new(@"Saved:\s+'(?<path>[^']+)'", RegexOptions.Compiled);
    
    // Time: 00:28.38 (Saving: 00:00.15)
    private static readonly Regex RxTime = new(@"Time:\s+(?<time>\d{2}:\d{2}\.\d{2})(?:\s+\(Saving:\s+(?<saving>\d{2}:\d{2}\.\d{2})\))?", RegexOptions.Compiled);
    
    // 开始渲染: Start rendering: Scene, ViewLayer
    private static readonly Regex RxStart = new(@"Start rendering:\s+(?<scene>[^,]+),\s+(?<layer>.+)", RegexOptions.Compiled);
    
    // 引擎: Engine: Cycles / Eevee / Workbench
    private static readonly Regex RxEngine = new(@"Engine:\s+(?<engine>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public InfoType? TryParseInfoType(string line)
    {
        if (RxCyclesSample.IsMatch(line) || 
            RxEeveeSample.IsMatch(line) || 
            RxSaved.IsMatch(line) ||
            RxTime.IsMatch(line) ||
            RxStart.IsMatch(line) ||
            RxEngine.IsMatch(line))
        {
            return InfoType.RenderProgress;
        }
        return null;
    }
    
    public RenderProgress? ParseInfo(string line)
    {
        // 解析采样信息
        var cyclesMatch = RxCyclesSample.Match(line);
        if (cyclesMatch.Success)
        {
            return new RenderProgress
            {
                SampleCurrent = int.Parse(cyclesMatch.Groups["cur"].Value),
                SampleTotal = int.Parse(cyclesMatch.Groups["tot"].Value)
            };
        }
        
        var eeveeMatch = RxEeveeSample.Match(line);
        if (eeveeMatch.Success)
        {
            return new RenderProgress
            {
                SampleCurrent = int.Parse(eeveeMatch.Groups["cur"].Value),
                SampleTotal = int.Parse(eeveeMatch.Groups["tot"].Value)
            };
        }
        
        // 解析保存信息
        var savedMatch = RxSaved.Match(line);
        if (savedMatch.Success)
        {
            return new RenderProgress
            {
                SavedPath = savedMatch.Groups["path"].Value
            };
        }
        
        // 解析时间信息
        var timeMatch = RxTime.Match(line);
        if (timeMatch.Success)
        {
            if (TimeSpan.TryParseExact(timeMatch.Groups["time"].Value, @"mm\:ss\.ff", CultureInfo.InvariantCulture, out var elapsed))
            {
                return new RenderProgress
                {
                    Elapsed = elapsed
                };
            }
        }
        
        // 解析开始渲染信息
        var startMatch = RxStart.Match(line);
        if (startMatch.Success)
        {
            return new RenderProgress
            {
                Scene = startMatch.Groups["scene"].Value,
                ViewLayer = startMatch.Groups["layer"].Value
            };
        }
        
        // 解析引擎信息
        var engineMatch = RxEngine.Match(line);
        if (engineMatch.Success)
        {
            var engineName = engineMatch.Groups["engine"].Value;
            if (Enum.TryParse<RenderEngine>(engineName, true, out var engine))
            {
                return new RenderProgress
                {
                    Engine = engine
                };
            }
        }
        
        return null;
    }
    
    public List<object> GenerateEvents(RenderProgress progress)
    {
        var events = new List<object>();
        
        if (progress.SampleCurrent.HasValue && progress.SampleTotal.HasValue)
        {
            events.Add(new RenderProgressEvent(progress));
        }
        
        if (!string.IsNullOrEmpty(progress.SavedPath))
        {
            events.Add(new RenderSaved(progress.SavedPath, progress.CurrentFrame));
        }
        
        return events;
    }
}
