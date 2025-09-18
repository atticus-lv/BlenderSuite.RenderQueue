using System;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Services.BlenderService;
using BlenderRenderQueue.Services.BlenderService.ServiceOutputParser;

namespace BlenderRenderQueue.ViewModels.Test;

public partial class TestRenderViewModel : ViewModelBase
{
	[ObservableProperty]
	private string _blenderPath = string.Empty;

	[ObservableProperty]
	private string _blendFilePath = string.Empty;

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

	[RelayCommand]
	private async Task BrowseBlender()
	{
		var path = await this.SelectFile("选择 Blender 可执行文件");
		if (!string.IsNullOrWhiteSpace(path)) BlenderPath = path;
	}

	[RelayCommand]
	private async Task BrowseBlendFile()
	{
		var path = await this.SelectFile("选择 .blend 文件");
		if (!string.IsNullOrWhiteSpace(path)) BlendFilePath = path;
	}

	[RelayCommand]
	private async Task StartRender()
	{
		if (string.IsNullOrWhiteSpace(BlenderPath) || string.IsNullOrWhiteSpace(BlendFilePath))
		{
			AppendLog("请先选择 Blender 路径和 .blend 文件");
			return;
		}

		DisposeSession();
		_exe = new BlenderExeService(BlenderPath);
		_session = new RenderSession(_exe, new RenderOutputParser());

		_session.OnProgress += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProgress(s));
		_session.OnEvent += e => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnEvent(e));

		AppendLog("已启动 Blender 进程，准备执行渲染脚本（示例环境，仅展示解析，不实际提交渲染）。");
		AppendLog("请将你的渲染调用脚本接入 BlenderExeService.ExecuteScript 或 CLI 参数。");
	}

	[RelayCommand]
	private void StopRender()
	{
		DisposeSession();
		AppendLog("已停止。");
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
				AppendLog(s.IsAnimation ? $"开始动画渲染: {s.StartFrame}..{s.EndFrame}" : $"开始单帧渲染");
				break;
			case RenderStarted rs:
				AppendLog($"开始帧 {rs.Frame} ({rs.Engine}) {rs.Scene},{rs.ViewLayer}");
				break;
			case RenderSaved saved:
				AppendLog($"已保存: {saved.Path} (帧 {saved.Frame})");
				break;
			case RenderCompletedFrame done:
				AppendLog($"帧 {done.Frame} 完成，用时 {done.Time}");
				break;
			case RenderCompletedAll:
				AppendLog("全部帧完成");
				break;
			case RenderError err:
				AppendLog($"错误: {err.Message}");
				break;
		}
	}

	private void AppendLog(string line)
	{
		OutputLog = string.IsNullOrEmpty(OutputLog) ? line : OutputLog + Environment.NewLine + line;
	}

	private void DisposeSession()
	{
		try { _session?.Dispose(); } catch { }
		try { _exe?.Dispose(); } catch { }
		_session = null;
		_exe = null;
	}
} 