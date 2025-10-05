using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.BlenderService.ProcessOutputParser.Core;

namespace BlenderRenderQueue.Services.Business.BlenderService.BlenderProcess;

/// <summary>
/// Blender视频生成进程 - 用于生成视频
/// </summary>
public class BlenderVideoProcess : BaseBlenderProcess
{
    public override BlenderProcessType ProcessType => BlenderProcessType.Video;

    public BlenderVideoProcess(string blenderPath) 
        : base(blenderPath, BlenderProcessConfig.CreateVideoConfig(), ParsePipelineFactory.CreateForVideo())
    {
    }

    /// <summary>
    /// 执行视频生成脚本（视频进程专用）
    /// </summary>
    public async Task<string> ExecuteVideoScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BlenderVideoProcess));
        if (!IsRunning) throw new InvalidOperationException("进程未运行");

        Console.WriteLine($"[BlenderVideoProcess] Executing video script - ID: {_processId}");

        var wrappedScript = $@"
exec('''
{script}
'''.strip())
print('__VIDEO_COMPLETE__')
";

        var outputBuilder = new StringBuilder();
        var completionSource = new TaskCompletionSource<bool>();

        void OutputHandler(string output)
        {
            if (output.Contains("__VIDEO_COMPLETE__"))
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
    /// 视频生成进程特有的错误处理逻辑
    /// </summary>
    protected override void HandleErrorOutput(string errorData)
    {
        // 视频生成进程的错误处理
        var isBlenderCrash = errorData.Contains("Blender quit", StringComparison.OrdinalIgnoreCase);
        var isAccessViolationCrash = errorData.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase);
        
        if (isBlenderCrash || isAccessViolationCrash)
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
