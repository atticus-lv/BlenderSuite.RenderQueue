using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using BlenderSuite.RenderQueue.Localizer;
using BlenderSuite.RenderQueue.Views;
using BlenderSuite.RenderQueue.Services.Application.Logging;

namespace BlenderSuite.RenderQueue.Helpers;

/// <summary>
/// 系统控制助手类，用于执行关机、重启等系统操作
/// </summary>
public static class SystemControlHelper
{
    /// <summary>
    /// 执行关机操作
    /// </summary>
    /// <param name="delaySeconds">延迟时间（秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功执行</returns>
    public static async Task<bool> ShutdownAsync(int delaySeconds = 60, CancellationToken cancellationToken = default)
    {
        try
        {
            var processStartInfo = CreateShutdownStartInfo(delaySeconds, restart: false);

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, "❌ Failed to start shutdown process", "SystemControlHelper");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode == 0)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"✅ Shutdown scheduled successfully in {delaySeconds} seconds", "SystemControlHelper");
                return true;
            }
            else
            {
                ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Shutdown command failed with exit code: {process.ExitCode}", "SystemControlHelper");
                return false;
            }
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Error executing shutdown: {ex.Message}", "SystemControlHelper");
            return false;
        }
    }

    /// <summary>
    /// 执行重启操作
    /// </summary>
    /// <param name="delaySeconds">延迟时间（秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功执行</returns>
    public static async Task<bool> RestartAsync(int delaySeconds = 60, CancellationToken cancellationToken = default)
    {
        try
        {
            var processStartInfo = CreateShutdownStartInfo(delaySeconds, restart: true);

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, "❌ Failed to start restart process", "SystemControlHelper");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode == 0)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"✅ Restart scheduled successfully in {delaySeconds} seconds", "SystemControlHelper");
                return true;
            }
            else
            {
                ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Restart command failed with exit code: {process.ExitCode}", "SystemControlHelper");
                return false;
            }
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Error executing restart: {ex.Message}", "SystemControlHelper");
            return false;
        }
    }

    /// <summary>
    /// 取消已计划的关机或重启操作
    /// </summary>
    /// <returns>是否成功取消</returns>
    public static async Task<bool> CancelShutdownAsync()
    {
        try
        {
            var processStartInfo = CreateCancelShutdownStartInfo();

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, "❌ Failed to start cancel shutdown process", "SystemControlHelper");
                return false;
            }

            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, "✅ Shutdown/restart cancelled successfully", "SystemControlHelper");
                return true;
            }
            else
            {
                ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Cancel shutdown command failed with exit code: {process.ExitCode}", "SystemControlHelper");
                return false;
            }
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"❌ Error cancelling shutdown: {ex.Message}", "SystemControlHelper");
            return false;
        }
    }

    /// <summary>
    /// 显示倒计时对话框
    /// </summary>
    /// <param name="actionType">操作类型（关机/重启）</param>
    /// <param name="countdownSeconds">倒计时秒数</param>
    /// <param name="parentWindow">父窗口</param>
    /// <returns>用户是否取消了操作</returns>
    public static async Task<bool> ShowCountdownDialogAsync(string actionType, int countdownSeconds, Window? parentWindow = null)
    {
        var dialog = new SystemActionCountdownView(actionType, countdownSeconds);
        return await dialog.ShowDialogAsync(parentWindow, countdownSeconds);
    }

    private static ProcessStartInfo CreateShutdownStartInfo(int delaySeconds, bool restart)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = restart ? $"/r /t {delaySeconds}" : $"/s /t {delaySeconds}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            var delayMinutes = Math.Max(1, (int)Math.Ceiling(delaySeconds / 60d));
            return new ProcessStartInfo
            {
                FileName = "/sbin/shutdown",
                Arguments = restart ? $"-r +{delayMinutes}" : $"-h +{delayMinutes}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        throw new PlatformNotSupportedException("Shutdown is not supported on this platform.");
    }

    private static ProcessStartInfo CreateCancelShutdownStartInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/a",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return new ProcessStartInfo
            {
                FileName = "/sbin/shutdown",
                Arguments = "-c",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        throw new PlatformNotSupportedException("Shutdown cancellation is not supported on this platform.");
    }
}
