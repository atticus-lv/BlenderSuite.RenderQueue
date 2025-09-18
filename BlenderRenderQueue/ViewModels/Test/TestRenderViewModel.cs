using System;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Services.BlenderService;
using BlenderRenderQueue.Services.BlenderService.ServiceOutputParser;
using System.Collections.Concurrent;
using Avalonia.Platform.Storage;
using System.Collections.Generic;

namespace BlenderRenderQueue.ViewModels.Test;

public partial class TestRenderViewModel : ViewModelBase
{
	[ObservableProperty]
	private string _blenderPath = string.Empty;

	[ObservableProperty]
	private string _blendFilePath = string.Empty;

	[ObservableProperty]
	private int _startFrame = 1;

	[ObservableProperty]
	private int _endFrame = 1;

	[ObservableProperty]
	private bool _animation = true;

	[ObservableProperty]
	private double _progress01;

	[ObservableProperty]
	private string _engine = string.Empty;

	[ObservableProperty]
	private int _currentFrame;

	[ObservableProperty]
	private string _sampleText = string.Empty;

	[ObservableProperty]
	private string _savedPath = string.Empty;

	[ObservableProperty]
	private string _outputLog = string.Empty;

	private IRenderSession? _session;
	private BlenderExeService? _exe;

	// 日志批量刷新
	private readonly ConcurrentQueue<string> _logQueue = new();
	private readonly System.Timers.Timer _logTimer;
	private const int MaxLogLines = 1000;
	private int _logLineCount = 0;

	public TestRenderViewModel()
	{
		_logTimer = new System.Timers.Timer(100);
		_logTimer.Elapsed += (_, __) => FlushLogQueue();
		_logTimer.AutoReset = true;
		_logTimer.Start();
	}

	[RelayCommand]
	private async Task BrowseBlender()
	{
		var path = await this.SelectFile("选择 Blender 可执行文件", GetBlenderExecutableFileTypes());
		if (!string.IsNullOrWhiteSpace(path)) BlenderPath = path;
	}

	[RelayCommand]
	private async Task BrowseBlendFile()
	{
		var path = await this.SelectFile("选择 blend 文件", GetBlendFileTypes());
		if (!string.IsNullOrWhiteSpace(path))
		{
			BlendFilePath = path;
			// 选择完文件后，立即查询场景帧范围
			try
			{
				if (_exe is null) _exe = new BlenderExeService(BlenderPath);
				var query = new BlenderQueryService();
				var (fs, fe) = await query.GetSceneFramesAsync(_exe, BlendFilePath);
				StartFrame = fs;
				EndFrame = fe;
				EnqueueLog($"获取场景帧范围: {fs}..{fe}");
			}
			catch (Exception ex)
			{
				EnqueueLog($"获取场景帧失败: {ex.Message}");
			}
		}
	}

	private static IEnumerable<FilePickerFileType> GetBlendFileTypes()
	{
		return new[]
		{
			new FilePickerFileType("Blend Files") { Patterns = new[] { "*.blend" } }
		};
	}

	private static IEnumerable<FilePickerFileType> GetBlenderExecutableFileTypes()
	{
		#if WINDOWS
		return new[] { new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } } };
		#else
		return new[] { new FilePickerFileType("Blender") { Patterns = new[] { "blender", "*blender*" } } };
		#endif
	}

	[RelayCommand]
	private async Task StartRender()
	{
		if (string.IsNullOrWhiteSpace(BlenderPath) || string.IsNullOrWhiteSpace(BlendFilePath))
		{
			EnqueueLog("请先选择 Blender 路径和 .blend 文件");
			return;
		}

		DisposeSession();
		_exe = new BlenderExeService(BlenderPath);
		_exe.OnOutputReceived += HandleRawOutput;
		_exe.OnErrorReceived += HandleRawError;

		_session = new RenderSession(_exe, new RenderOutputParser());
		_session.OnProgress += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProgress(s));
		_session.OnEvent += e => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnEvent(e));

		var cmd = new BlenderCommandService();
		await cmd.StartRenderAsync(_exe, BlendFilePath, StartFrame, EndFrame, Animation);
		EnqueueLog($"已发送渲染指令: {StartFrame}..{EndFrame}, animation={Animation}");
	}

	[RelayCommand]
	private void StopRender()
	{
		DisposeSession();
		EnqueueLog("已停止。");
	}

	private void HandleRawOutput(string line)
	{
		EnqueueLog($"[OUT] {line}");
	}

	private void HandleRawError(string line)
	{
		EnqueueLog($"[ERR] {line}");
	}

	private void OnProgress(RenderProgress p)
	{
		Engine = p.Engine.ToString();
		CurrentFrame = p.CurrentFrame;
		SampleText = p.SampleCurrent.HasValue && p.SampleTotal.HasValue ? $"{p.SampleCurrent}/{p.SampleTotal}" : string.Empty;
		SavedPath = p.SavedPath ?? string.Empty;

		if (p.SampleCurrent.HasValue && p.SampleTotal.HasValue && p.SampleTotal.Value > 0)
		{
			Progress01 = Math.Clamp((double)p.SampleCurrent.Value / p.SampleTotal.Value, 0, 1);
		}
		else
		{
			Progress01 = 0;
		}
	}

	private void OnEvent(RenderEvent e)
	{
		switch (e)
		{
			case RenderSessionStarted s:
				EnqueueLog(s.IsAnimation ? $"开始动画渲染: {s.StartFrame}..{s.EndFrame}" : $"开始单帧渲染");
				break;
			case RenderStarted rs:
				EnqueueLog($"开始帧 {rs.Frame} ({rs.Engine}) {rs.Scene},{rs.ViewLayer}");
				break;
			case RenderSaved saved:
				EnqueueLog($"已保存: {saved.Path} (帧 {saved.Frame})");
				break;
			case RenderCompletedFrame done:
				EnqueueLog($"帧 {done.Frame} 完成，用时 {done.Time}");
				break;
			case RenderCompletedAll:
				EnqueueLog("全部帧完成");
				break;
			case RenderError err:
				EnqueueLog($"错误: {err.Message}");
				break;
		}
	}

	private void EnqueueLog(string line)
	{
		_logQueue.Enqueue(line);
	}

	private void FlushLogQueue()
	{
		if (_logQueue.IsEmpty) return;
		var sb = new StringBuilder();
		int dequeued = 0;
		while (_logQueue.TryDequeue(out var line))
		{
			if (dequeued++ > 0) sb.AppendLine();
			sb.Append(line);
		}

		var text = sb.ToString();
		if (string.IsNullOrEmpty(text)) return;

		Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			// 将新文本追加到现有日志，并按行数限制截断最旧部分
			if (string.IsNullOrEmpty(OutputLog))
			{
				OutputLog = text;
				_logLineCount = CountLines(OutputLog);
			}
			else
			{
				OutputLog += Environment.NewLine + text;
				_logLineCount += CountLines(text);
			}

			if (_logLineCount > MaxLogLines)
			{
				// 只保留最后 MaxLogLines 行
				var lines = OutputLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
				var start = Math.Max(0, lines.Length - MaxLogLines);
				OutputLog = string.Join(Environment.NewLine, lines, start, lines.Length - start);
				_logLineCount = CountLines(OutputLog);
			}
		});
	}

	private static int CountLines(string s)
	{
		if (string.IsNullOrEmpty(s)) return 0;
		int count = 1;
		for (int i = 0; i < s.Length; i++) if (s[i] == '\n') count++;
		return count;
	}

	private void AppendLog(string line)
	{
		// 兼容旧调用：改为入队，走批量刷新
		EnqueueLog(line);
	}

	private void DisposeSession()
	{
		try { _session?.Dispose(); } catch { }
		if (_exe is not null)
		{
			_exe.OnOutputReceived -= HandleRawOutput;
			_exe.OnErrorReceived -= HandleRawError;
			try { _exe.Dispose(); } catch { }
		}
		_session = null;
		_exe = null;
	}
} 