using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

/// <summary>
/// Blender渲染进程 - 用于渲染任务
/// </summary>
public class BlenderRenderProcess : IBlenderProcess
{
    private readonly string _blenderPath;
    private readonly string _processId;
    private Process? _process;
    private bool _disposed;
    private bool _isRunning;

    public string ProcessId => _processId;
    public BlenderProcessType ProcessType => BlenderProcessType.Render;
    public string BlenderPath => _blenderPath;
    public bool IsRunning => _isRunning && _process != null && !_process.HasExited;
    public bool IsDisposed => _disposed;

    public event Action<string>? OnOutputReceived;
    public event Action<string>? OnErrorReceived;
    public event Action<int>? OnProcessExited;

    public BlenderRenderProcess(string blenderPath)
    {
        _blenderPath = blenderPath;
        _processId = Guid.NewGuid().ToString("N")[..8];
        Console.WriteLine($"[BlenderRenderProcess] Creating render process - ID: {_processId}, Path: {_blenderPath}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderRenderProcess));
        if (_isRunning) return;

        Console.WriteLine($"[BlenderRenderProcess] Starting render process - ID: {_processId}");

        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _blenderPath,
                    Arguments = "--background --factory-startup --log-level info --python-console",
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

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    OnOutputReceived?.Invoke(e.Data);
                }
            };

            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    // 渲染进程的错误处理更严格
                    var data = e.Data;
                    var isBlenderCrash = data.Contains("Blender quit", StringComparison.OrdinalIgnoreCase);
                    var isAccessViolationCrash = data.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase);
                    var isNoCameraError = data.Contains("Cannot render, no camera", StringComparison.OrdinalIgnoreCase);
                    
                    if (isBlenderCrash || isAccessViolationCrash || isNoCameraError)
                    {
                        OnErrorReceived?.Invoke($"Error: {data}");
                    }
                    else
                    {
                        // 其他情况作为警告处理
                        OnOutputReceived?.Invoke($"[WARN] {data}");
                    }
                }
            };

            _process.Exited += (_, e) =>
            {
                _isRunning = false;
                OnProcessExited?.Invoke(_process.ExitCode);
                Console.WriteLine($"[BlenderRenderProcess] Process exited - ID: {_processId}, ExitCode: {_process.ExitCode}");
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _isRunning = true;

            Console.WriteLine($"[BlenderRenderProcess] Render process started - ID: {_processId}, PID: {_process.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlenderRenderProcess] Failed to start render process - ID: {_processId}, Error: {ex.Message}");
            throw new InvalidOperationException($"启动Blender渲染进程失败: {ex.Message}", ex);
        }
    }

    public async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderRenderProcess));
        if (!IsRunning) throw new InvalidOperationException("进程未运行");

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
            await _process.StandardInput.FlushAsync();

            await completionSource.Task.WaitAsync(cancellationToken);
            return outputBuilder.ToString().TrimEnd();
        }
        finally
        {
            OnOutputReceived -= OutputHandler;
        }
    }

    /// <summary>
    /// 执行渲染脚本（渲染进程专用）
    /// </summary>
    public async Task<string> ExecuteRenderScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderRenderProcess));
        if (!IsRunning) throw new InvalidOperationException("进程未运行");

        Console.WriteLine($"[BlenderRenderProcess] Executing render script - ID: {_processId}");

        var wrappedScript = $@"
exec('''
{script}
'''.strip())
print('__RENDER_COMPLETE__')
";

        var outputBuilder = new StringBuilder();
        var completionSource = new TaskCompletionSource<bool>();

        void OutputHandler(string output)
        {
            if (output.Contains("__RENDER_COMPLETE__"))
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
            await _process.StandardInput.FlushAsync();

            await completionSource.Task.WaitAsync(cancellationToken);
            return outputBuilder.ToString().TrimEnd();
        }
        finally
        {
            OnOutputReceived -= OutputHandler;
        }
    }

    public async Task StopAsync()
    {
        if (_disposed || !_isRunning) return;

        Console.WriteLine($"[BlenderRenderProcess] Stopping render process - ID: {_processId}");

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
                await Task.Delay(2000); // 渲染进程需要更多时间退出
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlenderRenderProcess] Error stopping render process - ID: {_processId}, Error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Console.WriteLine($"[BlenderRenderProcess] Disposing render process - ID: {_processId}");

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
            Console.WriteLine($"[BlenderRenderProcess] Error disposing render process - ID: {_processId}, Error: {ex.Message}");
        }
        finally
        {
            _disposed = true;
            Console.WriteLine($"[BlenderRenderProcess] Render process disposed - ID: {_processId}");
        }
    }
}
