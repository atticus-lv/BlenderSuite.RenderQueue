using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.BlenderService;

/// <summary>
/// Blender进程适配器 - 将IBlenderProcess适配到BlenderExeService接口
/// </summary>
public class BlenderProcessAdapter : BlenderExeService
{
    private new readonly IBlenderProcess _process;

    public BlenderProcessAdapter(IBlenderProcess process) : base(process.BlenderPath)
    {
        _process = process;
    }

    public new string ServiceId => _process.ProcessId;

    public async Task<ScriptExecutionResult> ExecuteScript(string script, string operationName, CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await _process.ExecuteScriptAsync(script, cancellationToken);
            return new ScriptExecutionResult
            {
                Output = output,
                ExitCode = 0
            };
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult
            {
                Output = ex.Message,
                ExitCode = -1
            };
        }
    }

    public override void Dispose()
    {
        // 不释放底层进程，由调用者管理
        // _process.Dispose();
    }
}
