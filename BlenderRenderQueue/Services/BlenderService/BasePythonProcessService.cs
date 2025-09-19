using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

public delegate void OutputReceivedHandler(string message);

public class ScriptExecutionResult
{
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
}


public abstract class BasePythonProcessService : IDisposable
{
    public event OutputReceivedHandler? OnOutputReceived;
    public event OutputReceivedHandler? OnErrorReceived;
    public int Timeout { get; set; } = 60;

    protected Process? _process;
    protected bool _disposed;
    protected readonly SemaphoreSlim _executeLock = new(1, 1);


    protected virtual bool ValidateEnvironment()
    {
        return true; // 基类默认返回true，由子类实现具体验证逻辑
    }

    protected virtual void InitializeProcess()
    {
        if (!ValidateEnvironment()) throw new InvalidOperationException("环境验证失败");

        try
        {
            CreateProcess();
        }
        catch (Exception ex)
        {
            RaiseErrorReceived($"进程初始化失败: {ex.Message}");
            throw new InvalidOperationException($"进程初始化失败: {ex.Message}", ex);
        }
    }

    protected abstract void CreateProcess();

    protected virtual void RaiseOutputReceived(string message)
    {
        OnOutputReceived?.Invoke(message);
    }

    protected virtual void RaiseErrorReceived(string message)
    {
        OnErrorReceived?.Invoke(message);
    }

    protected async Task RestartProcessIfNeeded()
    {
        if (_process?.HasExited == true)
        {
            OnOutputReceived?.Invoke("进程已退出，正在重新启动...");
            _process?.Dispose();
            InitializeProcess();
            await Task.Delay(500);
        }
    }

    public async Task<ScriptExecutionResult> ExecuteScript(
        string script,
        string operationName,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        await _executeLock.WaitAsync(cancellationToken);
        try
        {
            progress?.Report(0.0);
            var result = new ScriptExecutionResult();
            var outputBuilder = new StringBuilder();

            await RestartProcessIfNeeded();

#if RELEASE
            OnOutputReceived?.Invoke($"执行任务: {operationName.Replace("_", " ")}");
#endif
            progress?.Report(0.4);

            var wrappedScript = $"""

                                 exec('''
                                 {script}
                                 '''.strip())
                                 print('__SCRIPT_COMPLETE__')

                                 """;

            var completionSource = new TaskCompletionSource<bool>();
            var lastActivityTime = DateTime.UtcNow;
            var activityTimeout = TimeSpan.FromSeconds(Timeout);

            void TempOutputHandler(string output)
            {
                // 只要有输出就更新活动时间
                lastActivityTime = DateTime.UtcNow;
                
                if (output.Contains("__SCRIPT_COMPLETE__"))
                    completionSource.TrySetResult(true);
                else
                    outputBuilder.AppendLine(output);
            }

            OnOutputReceived += TempOutputHandler;

            try
            {
                await _process!.StandardInput.WriteLineAsync(wrappedScript);
                await _process.StandardInput.FlushAsync();

                // 使用基于活动状态的超时检查
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                // 启动活动超时检查任务
                var activityCheckTask = Task.Run(async () =>
                {
                    while (!completionSource.Task.IsCompleted && !cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cts.Token); // 每秒检查一次
                        
                        if (DateTime.UtcNow - lastActivityTime > activityTimeout)
                        {
                            // 对于渲染操作，超时不应该立即取消，而是记录警告
                            if (operationName.Contains("render"))
                            {
                                OnOutputReceived?.Invoke($"[WARNING] 渲染无活动超过 {Timeout} 秒，但渲染可能仍在继续...");
                            }
                            else
                            {
                                OnErrorReceived?.Invoke($"操作超时 - 无活动超过 {Timeout} 秒: {operationName}");
                                cts.Cancel();
                                break;
                            }
                        }
                    }
                }, cts.Token);

                await completionSource.Task.WaitAsync(cts.Token);

                result.Output = outputBuilder.ToString().TrimEnd();
                return result;
            }
            catch (OperationCanceledException ex)
            {
                // 区分用户取消和超时
                if (cancellationToken.IsCancellationRequested)
                {
                    OnErrorReceived?.Invoke($"操作被用户取消: {operationName}");
                }
                else
                {
                    OnErrorReceived?.Invoke($"操作超时 - 无活动超过 {Timeout} 秒: {operationName}");
                }
                throw;
            }
            finally
            {
                OnOutputReceived -= TempOutputHandler;
            }
        }
        catch (Exception ex)
        {
            OnErrorReceived?.Invoke($"执行错误: {ex.Message}");
            throw;
        }
        finally
        {
            _executeLock.Release();
        }
    }

    public virtual async Task StopAsync()
    {
        if (_disposed) return;

        try
        {
            if (_process is not null && !_process.HasExited)
            {
                _process.Kill(true); // 确保终止进程及其所有子进程
                _process.Dispose();
                _process = null;
            }
        }
        catch (Exception ex)
        {
            RaiseErrorReceived($"停止进程时出错: {ex.Message}");
            throw;
        }
    }

    public virtual async Task RestartAsync()
    {
        try
        {
            await StopAsync();
            InitializeProcess();
            RaiseOutputReceived("进程已重新启动");
        }
        catch (Exception ex)
        {
            RaiseErrorReceived($"重启进程时出错: {ex.Message}");
            throw;
        }
    }

    public virtual void Dispose()
    {
        if (_disposed) return;

        try
        {
            _executeLock.Dispose();
            if (_process is not null && !_process.HasExited)
            {
                _process.Kill(true); // 修改这里确保彻底终止进程
                _process.Dispose();
            }
        }
        catch (Exception ex)
        {
            RaiseErrorReceived($"关闭进程时出错: {ex.Message}");
        }

        _disposed = true;
    }
}