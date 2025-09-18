using System;

namespace BlenderRenderQueue.Services.BlenderService;

using BlenderRenderQueue.Services.BlenderService.ServiceOutputParser;

public interface IRenderSession : IDisposable
{
	event Action<RenderEvent>? OnEvent;
	event Action<RenderProgress>? OnProgress;
	RenderProgress Latest { get; }
	void Cancel();
}

public sealed class RenderSession : IRenderSession
{
	private readonly BlenderExeService _process;
	private readonly IRenderOutputParser _parser;
	private bool _disposed;

	public event Action<RenderEvent>? OnEvent;
	public event Action<RenderProgress>? OnProgress;

	public RenderProgress Latest => _parser.Current;

	public RenderSession(BlenderExeService process, IRenderOutputParser parser)
	{
		_process = process;
		_parser = parser;
		_process.OnOutputReceived += HandleOutput;
		_process.OnErrorReceived += HandleError;
	}

	private void HandleOutput(string line)
	{
		var events = _parser.ParseLine(line);
		foreach (var e in events)
		{
			OnEvent?.Invoke(e);
			if (e is RenderProgressEvent pe)
			{
				OnProgress?.Invoke(pe.Progress);
			}
		}
	}

	private void HandleError(string msg)
	{
		OnEvent?.Invoke(new RenderError(msg));
	}

	public void Cancel()
	{
		try { _process.StopAsync().GetAwaiter().GetResult(); }
		catch { /* 交给上层决定是否处理异常 */ }
	}

	public void Dispose()
	{
		if (_disposed) return;
		_process.OnOutputReceived -= HandleOutput;
		_process.OnErrorReceived -= HandleError;
		_disposed = true;
	}
} 