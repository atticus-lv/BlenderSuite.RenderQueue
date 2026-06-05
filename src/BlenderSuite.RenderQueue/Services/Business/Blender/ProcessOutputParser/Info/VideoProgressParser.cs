using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser.Core;
using BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser.Models;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser.Info;

/// <summary>
/// 视频进度解析器
/// </summary>
public class VideoProgressParser : IInfoParser<VideoProgress>
{
    private static readonly Regex VideoWriteFrameRegex = new(@"ffmpeg: writing frame #(\d+) \((\d+)x(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex VideoAppendFrameRegex = new(@"Video append frame (\d+)", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"Time: (\d{2}:\d{2}\.\d{2})", RegexOptions.Compiled);
    private static readonly Regex ExecutingSequencerRegex = new(@"Executing sequencer", RegexOptions.Compiled);
    private static readonly Regex FFmpegClosingRegex = new(@"ffmpeg: closing", RegexOptions.Compiled);
    private static readonly Regex FFmpegFlushRegex = new(@"ffmpeg: flush delayed video frames", RegexOptions.Compiled);
    
    public InfoType? TryParseInfoType(string line)
    {
        if (VideoWriteFrameRegex.IsMatch(line) || 
            VideoAppendFrameRegex.IsMatch(line) ||
            TimeRegex.IsMatch(line) ||
            ExecutingSequencerRegex.IsMatch(line) ||
            FFmpegClosingRegex.IsMatch(line) ||
            FFmpegFlushRegex.IsMatch(line))
        {
            return InfoType.VideoProgress;
        }
        return null;
    }
    
    public VideoProgress? ParseInfo(string line)
    {
        var progress = new VideoProgress();
        
        // 解析视频写入帧信息
        var writeMatch = VideoWriteFrameRegex.Match(line);
        if (writeMatch.Success)
        {
            progress.CurrentFrame = int.Parse(writeMatch.Groups[1].Value);
            progress.Width = int.Parse(writeMatch.Groups[2].Value);
            progress.Height = int.Parse(writeMatch.Groups[3].Value);
            return progress;
        }
        
        // 解析视频追加帧信息
        var appendMatch = VideoAppendFrameRegex.Match(line);
        if (appendMatch.Success)
        {
            progress.CurrentFrame = int.Parse(appendMatch.Groups[1].Value);
            return progress;
        }
        
        // 解析时间信息
        var timeMatch = TimeRegex.Match(line);
        if (timeMatch.Success)
        {
            if (TimeSpan.TryParseExact(timeMatch.Groups[1].Value, @"mm\:ss\.ff", null, out var elapsed))
            {
                progress.Elapsed = elapsed;
            }
            return progress;
        }
        
        // 解析FFmpeg关闭信号
        if (FFmpegClosingRegex.IsMatch(line) || FFmpegFlushRegex.IsMatch(line))
        {
            progress.IsCompleted = true;
            return progress;
        }
        
        return null;
    }
    
    public List<object> GenerateEvents(VideoProgress progress)
    {
        var events = new List<object>();
        
        // 这里可以创建视频相关的事件
        // 例如：VideoFrameProcessedEvent, VideoCompletedEvent 等
        // 暂时返回空列表，后续可以根据需要添加
        
        return events;
    }
}
