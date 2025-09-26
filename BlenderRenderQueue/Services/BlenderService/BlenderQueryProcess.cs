using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

/// <summary>
/// Blender查询进程 - 用于查询文件属性、版本信息等
/// </summary>
public class BlenderQueryProcess : IBlenderProcess
{
    private readonly string _blenderPath;
    private readonly string _processId;
    private Process? _process;
    private bool _disposed;
    private bool _isRunning;

    public string ProcessId => _processId;
    public BlenderProcessType ProcessType => BlenderProcessType.Query;
    public string BlenderPath => _blenderPath;
    public bool IsRunning => _isRunning && _process != null && !_process.HasExited;
    public bool IsDisposed => _disposed;

    public event Action<string>? OnOutputReceived;
    public event Action<string>? OnErrorReceived;
    public event Action<int>? OnProcessExited;

    public BlenderQueryProcess(string blenderPath)
    {
        _blenderPath = blenderPath;
        _processId = Guid.NewGuid().ToString("N")[..8];
        Console.WriteLine($"[BlenderQueryProcess] Creating query process - ID: {_processId}, Path: {_blenderPath}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderQueryProcess));
        if (_isRunning) return;

        Console.WriteLine($"[BlenderQueryProcess] Starting query process - ID: {_processId}");

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
                    // 过滤非关键警告
                    var data = e.Data;
                    var isNonCriticalWarning = data.Contains("GPU functions for drawing are not available in background mode") ||
                                             data.Contains("invalid non-printable character U+FEFF") ||
                                             data.Contains("SystemError") ||
                                             data.Contains("SyntaxError") ||
                                             data.Contains("Traceback") ||
                                             data.Contains("Warning") ||
                                             data.Contains("DeprecationWarning");
                    
                    if (isNonCriticalWarning)
                    {
                        OnOutputReceived?.Invoke($"[INFO] {data}");
                    }
                    else
                    {
                        OnErrorReceived?.Invoke(data);
                    }
                }
            };

            _process.Exited += (_, e) =>
            {
                _isRunning = false;
                OnProcessExited?.Invoke(_process.ExitCode);
                Console.WriteLine($"[BlenderQueryProcess] Process exited - ID: {_processId}, ExitCode: {_process.ExitCode}");
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _isRunning = true;

            Console.WriteLine($"[BlenderQueryProcess] Query process started - ID: {_processId}, PID: {_process.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlenderQueryProcess] Failed to start query process - ID: {_processId}, Error: {ex.Message}");
            throw new InvalidOperationException($"启动Blender查询进程失败: {ex.Message}", ex);
        }
    }

    public async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderQueryProcess));
        if (!IsRunning) throw new InvalidOperationException("进程未运行");

        Console.WriteLine($"[BlenderQueryProcess] Executing script - ID: {_processId}");

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
            Console.WriteLine($"[BlenderQueryProcess] Output received - ID: {_processId}, Output: {output}");
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

            Console.WriteLine($"[BlenderQueryProcess] Script sent, waiting for completion - ID: {_processId}");
            await completionSource.Task.WaitAsync(cancellationToken);
            
            var result = outputBuilder.ToString().TrimEnd();
            Console.WriteLine($"[BlenderQueryProcess] Script completed - ID: {_processId}, Result length: {result.Length}");
            return result;
        }
        finally
        {
            OnOutputReceived -= OutputHandler;
        }
    }

    public async Task StopAsync()
    {
        if (_disposed || !_isRunning) return;

        Console.WriteLine($"[BlenderQueryProcess] Stopping query process - ID: {_processId}");

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
                await Task.Delay(1000); // 等待进程完全退出
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlenderQueryProcess] Error stopping query process - ID: {_processId}, Error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        Console.WriteLine($"[BlenderQueryProcess] Disposing query process - ID: {_processId}");

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
            Console.WriteLine($"[BlenderQueryProcess] Error disposing query process - ID: {_processId}, Error: {ex.Message}");
        }
        finally
        {
            _disposed = true;
            Console.WriteLine($"[BlenderQueryProcess] Query process disposed - ID: {_processId}");
        }
    }
}
