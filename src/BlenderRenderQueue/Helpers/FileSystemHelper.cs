using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Helpers;

/// <summary>
/// 文件系统操作辅助类
/// </summary>
public static class FileSystemHelper
{
    /// <summary>
    /// 在文件资源管理器中打开指定路径的文件夹
    /// 如果传入的是文件路径，则打开目录并选中该文件
    /// </summary>
    /// <param name="filePath">文件路径或目录路径</param>
    /// <returns>操作是否成功</returns>
    public static bool OpenFileDirectory(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Console.WriteLine("[FileSystemHelper] ❌ File path is null or empty");
                return false;
            }

            var normalizedPath = NormalizePathForCurrentPlatform(filePath);
            var isDirectory = Directory.Exists(normalizedPath);
            var isFile = File.Exists(normalizedPath);

            if (!isDirectory && !isFile)
            {
                Console.WriteLine($"[FileSystemHelper] ❌ Path does not exist: {filePath}");
                return false;
            }

            if (isFile)
            {
                var startInfo = CreateFileRevealStartInfo(normalizedPath);
                Console.WriteLine($"[FileSystemHelper] ✅ Opening directory and selecting file: {normalizedPath}");
                Process.Start(startInfo);
            }
            else
            {
                var startInfo = CreateDirectoryOpenStartInfo(normalizedPath);
                Console.WriteLine($"[FileSystemHelper] ✅ Opened directory in explorer: {normalizedPath}");
                Process.Start(startInfo);
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileSystemHelper] ❌ Error opening directory: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 检查是否可以打开指定文件路径的目录
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否可以打开目录</returns>
    public static bool CanOpenFileDirectory(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) && Directory.Exists(directory);
    }

    /// <summary>
    /// 使用系统默认程序播放视频文件
    /// </summary>
    /// <param name="videoPath">视频文件路径</param>
    /// <returns>操作是否成功</returns>
    public static bool PlayVideo(string videoPath)
    {
        try
        {
            if (string.IsNullOrEmpty(videoPath))
            {
                Console.WriteLine("[FileSystemHelper] ❌ Video path is null or empty");
                return false;
            }

            if (!File.Exists(videoPath))
            {
                Console.WriteLine($"[FileSystemHelper] ❌ Video file does not exist: {videoPath}");
                return false;
            }

            // 使用系统默认程序播放视频
            var startInfo = new ProcessStartInfo
            {
                FileName = videoPath,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            Console.WriteLine($"[FileSystemHelper] ✅ Playing video: {videoPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileSystemHelper] ❌ Error playing video: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 重启当前应用程序
    /// </summary>
    /// <returns>操作是否成功</returns>
    public static bool RestartApplication()
    {
        try
        {
            // 获取当前应用程序的可执行文件路径
            var currentExecutable = Environment.ProcessPath;

            if (string.IsNullOrEmpty(currentExecutable))
            {
                Console.WriteLine("[FileSystemHelper] ❌ Cannot get current executable path");
                return false;
            }

            if (!File.Exists(currentExecutable))
            {
                Console.WriteLine($"[FileSystemHelper] ❌ Current executable does not exist: {currentExecutable}");
                return false;
            }

            // 启动新的应用程序实例
            var startInfo = new ProcessStartInfo
            {
                FileName = currentExecutable,
                UseShellExecute = false
            };

            Process.Start(startInfo);
            Console.WriteLine($"[FileSystemHelper] ✅ Restarting application: {currentExecutable}");

            // 延迟退出，让UI有时间处理命令
            Task.Run(async () =>
            {
                await Task.Delay(100); // 等待1秒
                Environment.Exit(0);
            });

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FileSystemHelper] ❌ Error restarting application: {ex.Message}");
            return false;
        }
    }

    private static string NormalizePathForCurrentPlatform(string path)
    {
        return OperatingSystem.IsWindows() ? path.Replace('/', '\\') : path;
    }

    private static ProcessStartInfo CreateFileRevealStartInfo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { "-R", path },
                UseShellExecute = false
            };
        }

        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{Path.GetDirectoryName(path)}\"",
            UseShellExecute = false
        };
    }

    private static ProcessStartInfo CreateDirectoryOpenStartInfo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { path },
                UseShellExecute = false
            };
        }

        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false
        };
    }
}
