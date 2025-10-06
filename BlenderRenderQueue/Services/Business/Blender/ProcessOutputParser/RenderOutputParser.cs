using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser;

public sealed class RenderOutputParser : IRenderOutputParser
{
	private RenderProgress _current = new();
	public RenderProgress Current => _current;

	// Cycles 单帧: Rendering single frame (frame 1)
	private static readonly Regex RxSingleFrame = new(@"Rendering single frame \(frame (?<frame>\d+)\)", RegexOptions.Compiled);
	// 动画: Rendering animation (frames 1..10)
	private static readonly Regex RxAnimation = new(@"Rendering animation \(frames (?<start>\d+)\.\.(?<end>\d+)\)", RegexOptions.Compiled);
	// Start rendering: Scene, ViewLayer
	private static readonly Regex RxStart = new(@"Start rendering: (?<scene>[^,]+), (?<layer>.+)", RegexOptions.Compiled);
	// Engine: Cycles / Eevee / Workbench
	private static readonly Regex RxEngine = new(@"Engine:\s+(?<engine>\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
	// Cycles 采样行: Mem: 2983M | Sample 1/16
	private static readonly Regex RxCyclesSample = new(@"Mem:\s+(?<mem>\d+)M\s+\|\s+Sample\s+(?<cur>\d+)\/(?<tot>\d+)", RegexOptions.Compiled);
	// Eevee 采样行: Rendering 25 / 64 samples
	private static readonly Regex RxEeveeSample = new(@"Rendering\s+(?<cur>\d+)\s*\/\s*(?<tot>\d+)\s+samples", RegexOptions.Compiled | RegexOptions.IgnoreCase);
	// 保存: Saved: 'C:\tmp\0001.png'
	private static readonly Regex RxSaved = new(@"Saved:\s+'(?<path>[^']+)'", RegexOptions.Compiled);
	// Time: 00:28.38 (Saving: 00:00.15)
	private static readonly Regex RxTime = new(@"Time:\s+(?<time>\d{2}:\d{2}\.\d{2})(?:\s+\(Saving:\s+(?<saving>\d{2}:\d{2}\.\d{2})\))?", RegexOptions.Compiled);

	private bool _isAnimation;
	private int _currentFrame;
	private int? _startFrame;
	private int? _endFrame;
	private string? _scene;
	private string? _viewLayer;
	private RenderEngine _engine = RenderEngine.Unknown;

	public IReadOnlyList<RenderEvent> ParseLine(string line)
	{
		var events = new List<RenderEvent>();

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

		var mStart = RxStart.Match(line);
		if (mStart.Success)
		{
			_scene = mStart.Groups["scene"].Value.Trim();
			_viewLayer = mStart.Groups["layer"].Value.Trim();
			_current = _current with { Scene = _scene, ViewLayer = _viewLayer };
			events.Add(new RenderProgressEvent(_current));
			return events;
		}

		var mEngine = RxEngine.Match(line);
		if (mEngine.Success)
		{
			_engine = ParseEngine(mEngine.Groups["engine"].Value);
			_current = _current with { Engine = _engine };
			// 每次引擎识别后，紧接着通常是新一帧开始
			if (_isAnimation && _startFrame.HasValue && _endFrame.HasValue)
			{
				if (_currentFrame == 0) _currentFrame = _startFrame.Value;
				_current = _current with { CurrentFrame = _currentFrame };
				events.Add(new RenderStarted(_currentFrame, _scene, _viewLayer, _engine));
			}
			else if (!_isAnimation && _currentFrame != 0)
			{
				events.Add(new RenderStarted(_currentFrame, _scene, _viewLayer, _engine));
			}
			else
			{
				// 单帧但尚未解析到帧号时，先推送进度事件
				events.Add(new RenderProgressEvent(_current));
			}
			return events;
		}

		var mCycles = RxCyclesSample.Match(line);
		if (mCycles.Success)
		{
			var mem = double.TryParse(mCycles.Groups["mem"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var memVal) ? memVal : (double?)null;
			var cur = int.Parse(mCycles.Groups["cur"].Value, CultureInfo.InvariantCulture);
			var tot = int.Parse(mCycles.Groups["tot"].Value, CultureInfo.InvariantCulture);
			_current = _current with { MemoryMB = mem, SampleCurrent = cur, SampleTotal = tot };
			events.Add(new RenderProgressEvent(_current));
			return events;
		}

		var mEevee = RxEeveeSample.Match(line);
		if (mEevee.Success)
		{
			var cur = int.Parse(mEevee.Groups["cur"].Value, CultureInfo.InvariantCulture);
			var tot = int.Parse(mEevee.Groups["tot"].Value, CultureInfo.InvariantCulture);
			_current = _current with { SampleCurrent = cur, SampleTotal = tot };
			events.Add(new RenderProgressEvent(_current));
			return events;
		}

		var mSaved = RxSaved.Match(line);
		if (mSaved.Success)
		{
			var path = mSaved.Groups["path"].Value;
			_current = _current with { SavedPath = path };
			events.Add(new RenderSaved(path, _current.CurrentFrame));
			return events;
		}

		var mTime = RxTime.Match(line);
		if (mTime.Success)
		{
			var elapsed = ParseMinuteSecondFraction(mTime.Groups["time"].Value);
			var saving = mTime.Groups["saving"].Success ? ParseMinuteSecondFraction(mTime.Groups["saving"].Value) : (TimeSpan?)null;
			_current = _current with { Elapsed = elapsed };
			events.Add(new RenderCompletedFrame(_current.CurrentFrame, elapsed, saving));
			if (_isAnimation && _endFrame.HasValue)
			{
				if (_currentFrame < _endFrame.Value)
				{
					_currentFrame++;
					_current = _current with { CurrentFrame = _currentFrame, SampleCurrent = 0 };
				}
				else
				{
					events.Add(new RenderCompletedAll());
				}
			}
			return events;
		}

		return events;
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

	private static TimeSpan ParseMinuteSecondFraction(string s)
	{
		// 00:28.38 => mm:ss.ff
		var parts = s.Split(':');
		var minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
		var seconds = double.Parse(parts[1], CultureInfo.InvariantCulture);
		return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
	}

	private static RenderEngine ParseEngine(string name)
	{
		return name.ToLowerInvariant() switch
		{
			"cycles" => RenderEngine.Cycles,
			"eevee" => RenderEngine.Eevee,
			"workbench" => RenderEngine.Workbench,
			_ => RenderEngine.Unknown
		};
	}
}