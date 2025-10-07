using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using BlenderRenderQueue.Localizer;

namespace BlenderRenderQueue.Helpers;

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
            // 使用 Windows 的 shutdown 命令
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = $"/s /t {delaySeconds}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                Console.WriteLine("[SystemControlHelper] ❌ Failed to start shutdown process");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode == 0)
            {
                Console.WriteLine($"[SystemControlHelper] ✅ Shutdown scheduled successfully in {delaySeconds} seconds");
                return true;
            }
            else
            {
                Console.WriteLine($"[SystemControlHelper] ❌ Shutdown command failed with exit code: {process.ExitCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SystemControlHelper] ❌ Error executing shutdown: {ex.Message}");
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
            // 使用 Windows 的 shutdown 命令进行重启
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = $"/r /t {delaySeconds}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                Console.WriteLine("[SystemControlHelper] ❌ Failed to start restart process");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            
            if (process.ExitCode == 0)
            {
                Console.WriteLine($"[SystemControlHelper] ✅ Restart scheduled successfully in {delaySeconds} seconds");
                return true;
            }
            else
            {
                Console.WriteLine($"[SystemControlHelper] ❌ Restart command failed with exit code: {process.ExitCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SystemControlHelper] ❌ Error executing restart: {ex.Message}");
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
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/a",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                Console.WriteLine("[SystemControlHelper] ❌ Failed to start cancel shutdown process");
                return false;
            }

            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                Console.WriteLine("[SystemControlHelper] ✅ Shutdown/restart cancelled successfully");
                return true;
            }
            else
            {
                Console.WriteLine($"[SystemControlHelper] ❌ Cancel shutdown command failed with exit code: {process.ExitCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SystemControlHelper] ❌ Error cancelling shutdown: {ex.Message}");
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
        var tcs = new TaskCompletionSource<bool>();
        var cancellationTokenSource = new CancellationTokenSource();
        var isCancelled = false;

        // 创建倒计时对话框
        var dialog = new Window
        {
            Title = Localizer.Localizer.Instance["SystemControl_CountdownTitle"],
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            ShowInTaskbar = true
        };

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 15
        };

        var titleText = new TextBlock
        {
            Text = string.Format(Localizer.Localizer.Instance["SystemControl_CountdownMessage"], actionType),
            FontSize = 16,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var countdownText = new TextBlock
        {
            Text = countdownSeconds.ToString(),
            FontSize = 24,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = Avalonia.Media.Brushes.Red
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 10
        };

        var cancelButton = new Button
        {
            Content = Localizer.Localizer.Instance["SystemControl_Cancel"],
            Width = 100,
            Height = 35
        };

        cancelButton.Click += (s, e) =>
        {
            isCancelled = true;
            cancellationTokenSource.Cancel();
            dialog.Close();
            tcs.SetResult(true); // 返回 true 表示用户取消了操作
        };

        buttonPanel.Children.Add(cancelButton);
        stackPanel.Children.Add(titleText);
        stackPanel.Children.Add(countdownText);
        stackPanel.Children.Add(buttonPanel);
        dialog.Content = stackPanel;

        // 启动倒计时任务
        var countdownTask = Task.Run(async () =>
        {
            for (int i = countdownSeconds; i > 0 && !cancellationTokenSource.Token.IsCancellationRequested; i--)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    countdownText.Text = i.ToString();
                });
                
                try
                {
                    await Task.Delay(1000, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                // 倒计时结束，关闭对话框
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    dialog.Close();
                    tcs.SetResult(false); // 返回 false 表示倒计时结束，没有取消
                });
            }
        });

        // 显示对话框
        if (parentWindow != null)
        {
            await dialog.ShowDialog(parentWindow);
        }
        else
        {
            dialog.Show();
        }

        return await tcs.Task;
    }
}
