using System;
using System.Diagnostics;
using System.IO;

namespace BlenderRenderQueue.Helpers;

/// <summary>
/// 文件系统操作辅助类
/// </summary>
public static class FileSystemHelper
{
    /// <summary>
    /// 在文件资源管理器中打开指定路径的文件夹
    /// </summary>
    /// <param name="filePath">文件路径</param>
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

            // 获取文件所在的目录
            var directory = Path.GetDirectoryName(filePath);

            if (string.IsNullOrEmpty(directory))
            {
                Console.WriteLine("[FileSystemHelper] ❌ Cannot get directory from file path");
                return false;
            }

            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"[FileSystemHelper] ❌ Directory does not exist: {directory}");
                return false;
            }

            // 启动文件资源管理器
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory.Replace('/', '\\')}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            Process.Start(startInfo);
            Console.WriteLine($"[FileSystemHelper] ✅ Opened directory in explorer: {directory}");
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
}
