using System;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.IO;
using BlenderSuite.RenderQueue.Extensions;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.Services.Business.Blender.ProcessOutputParser;
using BlenderSuite.RenderQueue.Services.UI;
using Microsoft.Extensions.DependencyInjection;

namespace BlenderSuite.RenderQueue.ViewModels.Test;

public partial class TestRenderViewModel : ViewModelBase
{
	private const string BlenderValidationChannel = nameof(TestRenderViewModel);

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
	private double _overallProgress01;

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
	
	[ObservableProperty]
	private bool _isLogPaused = false;
	
	[ObservableProperty]
	private string _logPauseButtonText = "Stop Log";

	[ObservableProperty]
	private int _renderTimeoutSeconds = 300; // 默认5分钟无活动超时

	private IRenderSession? _session;
	private BlenderProcessService? _processService;

	[ObservableProperty]
	private BlendScenePropertiesViewModel _scenePropertiesViewModel = new(new BlenderQueryService());

	// 日志批量刷新
	private readonly ConcurrentQueue<string> _logQueue = new();
	private readonly System.Timers.Timer _logTimer;
	private const int MaxLogLines = 1000;
	private int _logLineCount = 0;
	
	// 性能优化相关
	private volatile bool _isFlushing = false;
	private readonly object _logLock = new object();
	private DateTime _lastFlushTime = DateTime.MinValue;
	private const int MinFlushIntervalMs = 50; // 最小刷新间隔50ms
	private const int MaxBatchSize = 100; // 单次处理最大日志条数

	private readonly IBlenderValidationService _blenderValidationService;

	public TestRenderViewModel()
	{
		_blenderValidationService = AppServices.Instance.GetService<IBlenderValidationService>()
			?? new BlenderValidationService(new BlenderCliInfoService());

		// 降低刷新频率，提高批量处理效率
		_logTimer = new System.Timers.Timer(200); // 从100ms改为200ms
		_logTimer.Elapsed += (_, __) => FlushLogQueue();
		_logTimer.AutoReset = true;
		_logTimer.Start();

		// Windows 上尝试自动定位 Blender（先快速查注册表，同步<~10ms）
		try
		{
			if (OperatingSystem.IsWindows())
			{
				if (BlenderSuite.RenderQueue.Helpers.BlenderLocator.TryFindBlenderExe(out var exe))
				{
					BlenderPath = exe;
					EnqueueLog($"自动检测到 Blender: {exe}");
				}
				else
				{
					// 未命中则后台异步扫描常见目录，避免阻塞 UI
					Task.Run(async () =>
					{
						var asyncExe = await BlenderSuite.RenderQueue.Helpers.BlenderLocator.FindBlenderExeAsync();
						if (!string.IsNullOrWhiteSpace(asyncExe))
						{
							Avalonia.Threading.Dispatcher.UIThread.Post(() =>
							{
								BlenderPath = asyncExe;
								EnqueueLog($"异步检测到 Blender: {asyncExe}");
							});
						}
					}).FireAndForget(
						source: nameof(TestRenderViewModel),
						message: "测试渲染页后台扫描 Blender 失败。");
				}
			}
		}
		catch { }
	}

	partial void OnBlenderPathChanged(string value)
	{
		var request = _blenderValidationService.BeginValidation(value, BlenderValidationChannel);
		if (_blenderValidationService.ValidatePreconditions(request) != null)
		{
			return;
		}

		LoadBlenderInfoAsync(value, request).FireAndForget(
			source: nameof(TestRenderViewModel),
			message: "测试渲染页后台查询 Blender 版本失败。");
	}

	private async Task LoadBlenderInfoAsync(string path, BlenderValidationRequest request)
	{
		var result = await _blenderValidationService.ValidateAsync(request);
		if (!_blenderValidationService.IsCurrent(request) ||
		    result.IsCanceled ||
		    result.Status == BlenderValidationStatus.Stale)
		{
			return;
		}

		Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (!_blenderValidationService.IsCurrent(request) ||
			    !string.Equals(BlenderPath, path, StringComparison.Ordinal))
			{
				return;
			}

			if (result.Status == BlenderValidationStatus.Success && result.VersionInfo != null)
			{
				var info = result.VersionInfo;
				EnqueueLog($"Blender 版本: {info.Version} | 平台: {info.Platform} | 分支: {info.Branch} | Hash: {info.Hash}");
				return;
			}

			EnqueueLog($"查询版本失败: {result.Message}");
		});
	}

	[RelayCommand]
	private async Task BrowseBlender()
	{
		var path = await this.SelectFile("Blender_SelectFileDialog", GetBlenderExecutableFileTypes());
		if (!string.IsNullOrWhiteSpace(path)) BlenderPath = path;
	}

	[RelayCommand]
	private async Task BrowseBlendFile()
	{
		var path = await this.SelectFile("选择 blend 文件", GetBlendFileTypes());
			if (!string.IsNullOrWhiteSpace(path))
			{
				BlendFilePath = path;
				// 选择完文件后，通过FilePropertiesViewModel加载所有属性
				try
				{
					_processService ??= new BlenderProcessService(BlenderPath);
					EnqueueLog("[QUERY] 开始加载文件属性...");
					await ScenePropertiesViewModel.LoadPropertiesAsync(BlenderPath, BlendFilePath);

					// 从FilePropertiesViewModel获取帧范围信息
					StartFrame = ScenePropertiesViewModel.SceneProperties.FrameStart;
					EndFrame = ScenePropertiesViewModel.SceneProperties.FrameEnd;
					EnqueueLog($"[QUERY] 文件属性加载完成: 帧范围 {StartFrame}..{EndFrame}");
				}
				catch (Exception ex)
				{
					EnqueueLog($"[QUERY] 加载文件属性失败: {ex.Message}");
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
		if (OperatingSystem.IsWindows())
		{
			return new[] { new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } } };
		}

		if (OperatingSystem.IsMacOS())
		{
			return new[]
			{
				new FilePickerFileType("Blender")
				{
					Patterns = new[] { "Blender", "*Blender*" },
					AppleUniformTypeIdentifiers = new[] { "public.unix-executable" }
				}
			};
		}

		return new[] { new FilePickerFileType("Executable") { Patterns = new[] { "*" } } };
	}

	[RelayCommand]
	private async Task StartRender()
	{
		if (string.IsNullOrWhiteSpace(BlenderPath) || string.IsNullOrWhiteSpace(BlendFilePath))
		{
			EnqueueLog("Blender_SelectExecutable");
			return;
		}

		DisposeSession();
		_processService = new BlenderProcessService(BlenderPath);
		// 注意：新架构中，事件处理由RenderSession负责
		// _processService.OnOutputReceived += HandleRawOutput;
		// _processService.OnErrorReceived += HandleRawError;

		// 创建渲染进程并获取会话
		var renderProcess = await _processService.CreateRenderProcessAsync();
		_session = new RenderSession(renderProcess, new RenderOutputParser());
		_session.OnProgress += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProgress(s));
		_session.OnEvent += e => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnEvent(e));

		var cmd = new BlenderCommandService();
		
		try
		{
			// 为渲染任务设置可配置的超时时间
			// 注意：新架构中，超时管理由RenderTaskViewModel处理
			// _processService.Timeout = RenderTimeoutSeconds;

			EnqueueLog($"开始渲染: {StartFrame}..{EndFrame}, animation={Animation} (无活动超时: {RenderTimeoutSeconds}秒)");
			await cmd.StartRenderAsync(renderProcess, BlendFilePath, Animation, StartFrame, EndFrame);
			EnqueueLog($"渲染指令已发送完成");
		}
		catch (TaskCanceledException ex)
		{
			if (ex.CancellationToken.IsCancellationRequested)
			{
				EnqueueLog("渲染任务被用户取消");
			}
			else
			{
				EnqueueLog($"渲染任务超时: {ex.Message}");
			}
		}
		catch (OperationCanceledException ex)
		{
			EnqueueLog($"渲染操作被取消: {ex.Message}");
		}
		catch (Exception ex)
		{
			EnqueueLog($"渲染启动失败: {ex.Message}");
		}
	}

	[RelayCommand]
	private void StopRender()
	{
		DisposeSession();
		EnqueueLog("已停止。");
	}
	
	[RelayCommand]
	private void ClearLog()
	{
		OutputLog = string.Empty;
		_logLineCount = 0;
		// 清空队列中的待处理日志
		while (_logQueue.TryDequeue(out _)) { }
		EnqueueLog("日志已清空");
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

		// 计算整体进度（基于帧范围 + 单帧进度）
		var totalFrames = Math.Max(0, EndFrame - StartFrame + 1);
		if (totalFrames > 0)
		{
			var completedFrames = Math.Max(0, p.CurrentFrame - StartFrame);
			double perFrame = Progress01; // 当前帧内进度
			OverallProgress01 = Math.Clamp((completedFrames + perFrame) / totalFrames, 0, 1);
		}
		else
		{
			OverallProgress01 = 0;
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
				OverallProgress01 = 1;
				break;
			case RenderError err:
				EnqueueLog($"错误: {err.Message}");
				break;
		}
	}

	private void EnqueueLog(string line)
	{
		if (IsLogPaused) return;
		
		// 简单的重复日志过滤
		if (!string.IsNullOrWhiteSpace(line) && _logQueue.Count < 500) // 防止队列过大
		{
			_logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
		}
	}

	private void FlushLogQueue()
	{
		if (_logQueue.IsEmpty || IsLogPaused || _isFlushing) return;
		
		// 防止频繁刷新
		var now = DateTime.Now;
		if ((now - _lastFlushTime).TotalMilliseconds < MinFlushIntervalMs) return;
		
		lock (_logLock)
		{
			if (_isFlushing) return;
			_isFlushing = true;
		}
		
		try
		{
			var sb = new StringBuilder();
			int dequeued = 0;
			
			// 限制单次处理的日志数量，避免UI阻塞
			while (_logQueue.TryDequeue(out var line) && dequeued < MaxBatchSize)
			{
				if (dequeued++ > 0) sb.AppendLine();
				sb.Append(line);
			}

			var text = sb.ToString();
			if (string.IsNullOrEmpty(text)) return;

			_lastFlushTime = now;
			
			// 使用低优先级调度，减少对UI的影响
			Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				UpdateOutputLog(text);
			}, Avalonia.Threading.DispatcherPriority.Background);
		}
		finally
		{
			_isFlushing = false;
		}
	}
	
	private void UpdateOutputLog(string newText)
	{
		// 将新文本追加到现有日志，并按行数限制截断最旧部分
		if (string.IsNullOrEmpty(OutputLog))
		{
			OutputLog = newText;
			_logLineCount = CountLines(OutputLog);
		}
		else
		{
			OutputLog += Environment.NewLine + newText;
			_logLineCount += CountLines(newText);
		}

		if (_logLineCount > MaxLogLines)
		{
			// 只保留最后 MaxLogLines 行，使用更高效的字符串操作
			var lines = OutputLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
			var start = Math.Max(0, lines.Length - MaxLogLines);
			OutputLog = string.Join(Environment.NewLine, lines, start, lines.Length - start);
			_logLineCount = MaxLogLines;
		}
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
		if (_processService is not null)
		{
			// _processService.OnOutputReceived -= HandleRawOutput;
			// _processService.OnErrorReceived -= HandleRawError;
			try { _processService.Dispose(); } catch { }
		}
		_session = null;
		_processService = null;
	}
} 
