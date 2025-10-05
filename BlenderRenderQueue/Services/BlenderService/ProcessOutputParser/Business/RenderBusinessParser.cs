using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.BlenderService.ProcessOutputParser.Business;

/// <summary>
/// 渲染业务解析器 - 将基础解析结果转换为渲染业务事件
/// </summary>
public class RenderBusinessParser : IBusinessParser<RenderEvent>
{
    private RenderProgress _current = new();
    private bool _isAnimation;
    private int _currentFrame;
    private int? _startFrame;
    private int? _endFrame;
    private string? _scene;
    private string? _viewLayer;
    private RenderEngine _engine = RenderEngine.Unknown;

    // 渲染相关的正则表达式
    private static readonly Regex RxSingleFrame = new(@"Rendering single frame \(frame (?<frame>\d+)\)", RegexOptions.Compiled);
    private static readonly Regex RxAnimation = new(@"Rendering animation \(frames (?<start>\d+)\.\.(?<end>\d+)\)", RegexOptions.Compiled);
    private static readonly Regex RxStart = new(@"Start rendering: (?<scene>[^,]+), (?<layer>.+)", RegexOptions.Compiled);
    private static readonly Regex RxEngine = new(@"Engine:\s+(?<engine>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public List<RenderEvent> ParseBusinessEvents(string line)
    {
        var events = new List<RenderEvent>();

        // 解析动画渲染
        var mAnim = RxAnimation.Match(line);
        if (mAnim.Success)
        {
            _isAnimation = true;
            _startFrame = int.Parse(mAnim.Groups["start"].Value, CultureInfo.InvariantCulture);
            _endFrame = int.Parse(mAnim.Groups["end"].Value, CultureInfo.InvariantCulture);
            _current = new RenderProgress
            {
                StartFrame = _startFrame,
                EndFrame = _endFrame,
                CurrentFrame = _startFrame ?? 0,
                Engine = _engine,
                Scene = _scene,
                ViewLayer = _viewLayer
            };
            events.Add(new RenderSessionStarted(true, _startFrame, _endFrame));
            return events;
        }

        // 解析单帧渲染
        var mSingle = RxSingleFrame.Match(line);
        if (mSingle.Success)
        {
            _isAnimation = false;
            _currentFrame = int.Parse(mSingle.Groups["frame"].Value, CultureInfo.InvariantCulture);
            _current = new RenderProgress
            {
                CurrentFrame = _currentFrame,
                Engine = _engine,
                Scene = _scene,
                ViewLayer = _viewLayer
            };
            events.Add(new RenderSessionStarted(false, _currentFrame, _currentFrame));
            return events;
        }

        // 解析开始渲染
        var mStart = RxStart.Match(line);
        if (mStart.Success)
        {
            _scene = mStart.Groups["scene"].Value.Trim();
            _viewLayer = mStart.Groups["layer"].Value.Trim();
            _current = _current with { Scene = _scene, ViewLayer = _viewLayer };
            events.Add(new RenderStarted(_current.CurrentFrame, _scene, _viewLayer, _current.Engine));
            return events;
        }

        // 解析引擎信息
        var mEngine = RxEngine.Match(line);
        if (mEngine.Success)
        {
            var engineName = mEngine.Groups["engine"].Value;
            if (Enum.TryParse<RenderEngine>(engineName, true, out var engine))
            {
                _engine = engine;
                _current = _current with { Engine = engine };
            }
            return events;
        }

        return events;
    }

    public object? GetCurrentState()
    {
        return _current;
    }

    public void Reset()
    {
        _current = new RenderProgress();
        _isAnimation = false;
        _currentFrame = 0;
        _startFrame = null;
        _endFrame = null;
        _scene = null;
        _viewLayer = null;
        _engine = RenderEngine.Unknown;
    }
}
