using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

namespace BlenderRenderQueue.Services.Business.Blender.BlenderProcess;

/// <summary>
/// Blender进程基类 - 包含所有进程的公共逻辑
/// </summary>
public abstract class BaseBlenderProcess : IBlenderProcess
{
    private readonly string _blenderPath;
    protected readonly string _processId;
    private readonly BlenderProcessConfig _config;
    private readonly ParsePipeline _parsePipeline;
    protected Process? _process;
    protected bool _disposed;
    private bool _isRunning;

    public string ProcessId => _processId;
    public abstract BlenderProcessType ProcessType { get; }
    public string BlenderPath => _blenderPath;
    public bool IsRunning => _isRunning && _process is { HasExited: false };
    public bool IsDisposed => _disposed;

    public event Action<string>? OnOutputReceived;
    public event Action<string>? OnErrorReceived;
    public event Action<int>? OnProcessExited;

    protected BaseBlenderProcess(string blenderPath, BlenderProcessConfig config, ParsePipeline? parsePipeline = null)
    {
        _blenderPath = blenderPath;
        _processId = Guid.NewGuid().ToString("N")[..8];
        _config = config;
        _parsePipeline = parsePipeline ?? ParsePipelineFactory.CreateDefault();
        Console.WriteLine($"[{GetType().Name}] Creating {ProcessType} process - ID: {_processId}, Path: {_blenderPath}");
    }

    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        if (_isRunning) return;

        Console.WriteLine($"[{GetType().Name}] Starting {ProcessType} process - ID: {_processId}");

        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _blenderPath,
                    Arguments = _config.GetStartupArguments(),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardInputEncoding = Encoding.UTF8
                }
            };

            // 设置输出事件处理
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    ProcessOutputLine(e.Data);
                }
            };

            // 设置错误事件处理 - 子类可以重写错误处理逻辑
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    ProcessOutputLine(e.Data);
                }
            };

            // 设置进程退出事件
            _process.Exited += (_, e) =>
            {
                _isRunning = false;
                OnProcessExited?.Invoke(_process.ExitCode);
                Console.WriteLine($"[{GetType().Name}] Process exited - ID: {_processId}, ExitCode: {_process.ExitCode}");
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _isRunning = true;

            Console.WriteLine($"[{GetType().Name}] {ProcessType} process started - ID: {_processId}, PID: {_process.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{GetType().Name}] Failed to start {ProcessType} process - ID: {_processId}, Error: {ex.Message}");
            throw new InvalidOperationException($"启动Blender {ProcessType}进程失败: {ex.Message}", ex);
        }
    }

    public virtual async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        if (!IsRunning) throw new InvalidOperationException("进程未运行");

        Console.WriteLine($"[{GetType().Name}] Executing script - ID: {_processId}");

        var wrappedScript = $@"
exec('''
{script}
'''.strip())
print('__SCRIPT_COMPLETE__')
";

        var outputBuilder = new StringBuilder();
        var completionSource = new TaskCompletionSource<bool>();

        void OutputHandler(string output)
        {
            // Console.WriteLine($"[{GetType().Name}] Output received - ID: {_processId}, Output: {output}");
            if (output.Contains("__SCRIPT_COMPLETE__"))
            {
                completionSource.TrySetResult(true);
            }
            else
            {
                outputBuilder.AppendLine(output);
            }
        }

        OnOutputReceived += OutputHandler;

        try
        {
            await _process!.StandardInput.WriteLineAsync(wrappedScript);
            await _process.StandardInput.FlushAsync(cancellationToken);

            Console.WriteLine($"[{GetType().Name}] Script sent, waiting for completion - ID: {_processId}");
            await completionSource.Task.WaitAsync(cancellationToken);
            
            var result = outputBuilder.ToString().TrimEnd();
            Console.WriteLine($"[{GetType().Name}] Script completed - ID: {_processId}, Result length: {result.Length}");
            return result;
        }
        finally
        {
            OnOutputReceived -= OutputHandler;
        }
    }

    public virtual async Task StopAsync()
    {
        if (_disposed || !_isRunning) return;

        Console.WriteLine($"[{GetType().Name}] Stopping {ProcessType} process - ID: {_processId}");

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(true);
                await Task.Delay(_config.StopWaitTimeMs); // 使用配置的等待时间
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{GetType().Name}] Error stopping {ProcessType} process - ID: {_processId}, Error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    public virtual void Dispose()
    {
        if (_disposed) return;

        Console.WriteLine($"[{GetType().Name}] Disposing {ProcessType} process - ID: {_processId}");

        try
        {
            StopAsync().GetAwaiter().GetResult();
            
            if (_process != null)
            {
                _process.Dispose();
                _process = null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{GetType().Name}] Error disposing {ProcessType} process - ID: {_processId}, Error: {ex.Message}");
        }
        finally
        {
            _disposed = true;
            Console.WriteLine($"[{GetType().Name}] {ProcessType} process disposed - ID: {_processId}");
        }
    }

    /// <summary>
    /// 处理输出行 - 使用新的解析器架构
    /// </summary>
    /// <param name="line">输出行</param>
    protected virtual void ProcessOutputLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
            
        // 对于查询进程，直接传递所有输出，不进行解析
        if (ProcessType == BlenderProcessType.Query)
        {
            RaiseOutputReceived(line);
            return;
        }
            
        // 对于渲染和视频进程，先传递原始输出给订阅者（如RenderSession）
        // 然后进行新解析器架构的处理
        RaiseOutputReceived(line);
        
        try
        {
            var result = _parsePipeline.ParseLine(line);
            
            // 根据解析结果决定是否还需要额外的错误处理
            if (result.HasError)
            {
                // 对于错误，除了原始输出外，还可以触发错误事件
                RaiseErrorReceived(result.ProcessedLine);
            }
            
            // 触发特定事件
            foreach (var evt in result.Events)
            {
                // 这里可以根据需要触发特定的事件
                // 例如：OnRenderProgress?.Invoke(evt as RenderProgressEvent);
            }
        }
        catch (Exception ex)
        {
            // 解析失败时，原始输出已经传递了，这里只记录错误
            Console.WriteLine($"[{GetType().Name}] Parse error for line: {line}, Error: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理错误输出 - 子类可以重写此方法来实现特定的错误处理逻辑
    /// </summary>
    /// <param name="errorData">错误数据</param>
    protected virtual void HandleErrorOutput(string errorData)
    {
        // 这个方法现在主要用于向后兼容，实际处理由 ProcessOutputLine 完成
        ProcessOutputLine(errorData);
    }

    /// <summary>
    /// 触发输出接收事件 - 子类可以使用此方法触发事件
    /// </summary>
    /// <param name="message">输出消息</param>
    protected void RaiseOutputReceived(string message)
    {
        OnOutputReceived?.Invoke(message);
    }

    /// <summary>
    /// 触发错误接收事件 - 子类可以使用此方法触发事件
    /// </summary>
    /// <param name="message">错误消息</param>
    protected void RaiseErrorReceived(string message)
    {
        OnErrorReceived?.Invoke(message);
    }
}
