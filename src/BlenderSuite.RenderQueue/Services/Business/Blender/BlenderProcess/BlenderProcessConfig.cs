using System;

namespace BlenderSuite.RenderQueue.Services.Business.Blender.BlenderProcess;

/// <summary>
/// Blender进程配置
/// </summary>
public class BlenderProcessConfig
{
    /// <summary>
    /// 是否使用工厂启动模式（--factory-startup）
    /// </summary>
    private bool UseFactoryStartup { get; set; }

    /// <summary>
    /// 日志级别
    /// </summary>
    public string LogLevel { get; set; } = "info";

    /// <summary>
    /// 是否启用Python控制台
    /// </summary>
    public bool EnablePythonConsole { get; set; } = true;

    /// <summary>
    /// 进程停止等待时间（毫秒）
    /// </summary>
    public int StopWaitTimeMs { get; set; } = 200;

    /// <summary>
    /// Python 控制台脚本执行超时时间。
    /// </summary>
    public TimeSpan ScriptExecutionTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 获取启动参数
    /// </summary>
    public string GetStartupArguments()
    {
        var args = "--background";

        if (UseFactoryStartup)
        {
            args += " --factory-startup";
        }

        args += " --log-level info";
        args += " --python-console";

        return args;
    }

    /// <summary>
    /// 创建查询进程配置
    /// </summary>
    public static BlenderProcessConfig CreateQueryConfig()
    {
        return new BlenderProcessConfig
        {
            UseFactoryStartup = true,
            StopWaitTimeMs = 100,
            ScriptExecutionTimeout = TimeSpan.FromMinutes(2)
        };
    }

    /// <summary>
    /// 创建渲染进程配置
    /// </summary>
    public static BlenderProcessConfig CreateRenderConfig()
    {
        return new BlenderProcessConfig
        {
            UseFactoryStartup = false,
            StopWaitTimeMs = 200,
            ScriptExecutionTimeout = TimeSpan.FromHours(6)
        };
    }

    /// <summary>
    /// 创建视频进程配置
    /// </summary>
    public static BlenderProcessConfig CreateVideoConfig()
    {
        return new BlenderProcessConfig
        {
            UseFactoryStartup = false,
            StopWaitTimeMs = 150,
            ScriptExecutionTimeout = TimeSpan.FromHours(6)
        };
    }
}
