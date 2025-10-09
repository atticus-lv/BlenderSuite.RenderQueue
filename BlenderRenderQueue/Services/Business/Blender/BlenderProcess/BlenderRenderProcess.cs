using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser.Core;

namespace BlenderRenderQueue.Services.Business.Blender.BlenderProcess;

/// <summary>
/// Blender渲染进程 - 用于渲染任务
/// </summary>
public class BlenderRenderProcess : BaseBlenderProcess
{
    public override BlenderProcessType ProcessType => BlenderProcessType.Render;

    public BlenderRenderProcess(string blenderPath) 
        : base(blenderPath, BlenderProcessConfig.CreateRenderConfig(), ParsePipelineFactory.CreateForRender())
    {
    }

    /// <summary>
    /// 执行渲染脚本（渲染进程专用）
    /// </summary>
    public async Task<string> ExecuteRenderScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderRenderProcess));
        if (!IsRunning) throw new InvalidOperationException("进程未运行");

        // Console.WriteLine($"[BlenderRenderProcess] Executing render script - ID: {_processId}");

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

    /// <summary>
    /// 渲染进程特有的错误处理逻辑
    /// </summary>
    protected override void HandleErrorOutput(string errorData)
    {
        // 渲染进程的错误处理更严格
        var isBlenderCrash = errorData.Contains("Blender quit", StringComparison.OrdinalIgnoreCase);
        var isAccessViolationCrash = errorData.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase);
        var isNoCameraError = errorData.Contains("Cannot render, no camera", StringComparison.OrdinalIgnoreCase);
        
        if (isBlenderCrash || isAccessViolationCrash || isNoCameraError)
        {
            RaiseErrorReceived($"Error: {errorData}");
        }
        else
        {
            // 其他情况作为警告处理
            RaiseOutputReceived($"[WARN] {errorData}");
        }
    }
}
