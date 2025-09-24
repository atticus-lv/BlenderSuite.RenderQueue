using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;

namespace BlenderRenderQueue.Services.FFmpegService;

/// <summary>
/// FFmpeg 服务实现
/// </summary>
public class FFmpegService : IFFmpegService
{
    private static readonly string[] SupportedImageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tiff", "*.tga" };
    private string? _ffmpegPath;

    public string? FFmpegPath => _ffmpegPath;

    public void SetFFmpegPath(string? ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        
        // 如果指定了 FFmpeg 路径，设置 FFMpegCore 的全局配置
        if (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath))
        {
            var directory = Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrEmpty(directory))
            {
                GlobalFFOptions.Current.BinaryFolder = directory;
            }
        }
    }

    public async Task<bool> GenerateVideoFromImagesAsync(
        string inputDirectory,
        string outputVideoPath,
        double fps,
        Action<double>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(inputDirectory))
            {
                throw new DirectoryNotFoundException($"输入目录不存在: {inputDirectory}");
            }

            // 自动检测图片格式和文件
            var (imageFiles, imagePattern) = DetectImageFiles(inputDirectory);
            
            if (imageFiles.Length == 0)
            {
                throw new InvalidOperationException($"在目录 {inputDirectory} 中未找到支持的图片文件 (PNG, JPG, JPEG, BMP, TIFF, TGA)");
            }
            
            // 获取总帧数，用于计算百分比
            var totalFrames = imageFiles.Length;
            
            // 计算视频总时长（秒）
            var totalDuration = TimeSpan.FromSeconds(totalFrames / fps);

            // 确保输出目录存在
            var outputDir = Path.GetDirectoryName(outputVideoPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 调试信息：记录找到的文件和模式
            Console.WriteLine($"找到 {imageFiles.Length} 个图片文件");
            Console.WriteLine($"前几个文件: {string.Join(", ", imageFiles.Take(3).Select(Path.GetFileName))}");
            
            if (imagePattern == "FILE_LIST")
            {
                // 使用文件列表方式处理不连续编号的文件
                Console.WriteLine("检测到不连续的文件编号，使用文件列表方式");
                
                // 创建临时文件列表
                var tempFileList = Path.GetTempFileName();
                try
                {
                    // 写入文件列表到临时文件，使用正确的 concat demuxer 格式
                    // 使用UTF-8编码确保中文字符正确处理
                    var fileListContent = imageFiles.Select(f => 
                    {
                        // 将Windows路径转换为Unix格式，并确保路径被正确引用
                        var unixPath = f.Replace("\\", "/");
                        // 转义单引号
                        var escapedPath = unixPath.Replace("'", "'\"'\"'");
                        return $"file '{escapedPath}'";
                    }).ToList();
                    fileListContent.Add(""); // 添加空行
                    
                    Console.WriteLine($"[FFmpegService] 写入文件列表到: {tempFileList}");
                    Console.WriteLine($"[FFmpegService] 文件列表内容示例: {string.Join("\n", fileListContent.Take(3))}");
                    
                    await File.WriteAllLinesAsync(tempFileList, fileListContent, System.Text.Encoding.UTF8, cancellationToken);
                    
                    // 使用 concat demuxer，让 FFmpeg 自动处理色彩空间
                    await FFMpegArguments
                        .FromFileInput(tempFileList, false, options => options
                            .WithCustomArgument("-f concat -safe 0"))
                        .OutputToFile(outputVideoPath, true, options => options
                            .WithVideoCodec(VideoCodec.LibX264)
                            .ForcePixelFormat("yuv420p")
                            .WithFramerate(fps)
                            .WithCustomArgument("-crf 0")  // 无损压缩
                            .WithCustomArgument("-g 18")  // GOP大小
                            .WithCustomArgument("-preset slow"))  // 高质量编码
                        .CancellableThrough(cancellationToken)
                        .NotifyOnProgress(progress => 
                        {
                            // 调试：输出原始进度值
                            Console.WriteLine($"[FFmpegService] Progress: {progress:F2}%");
                            progressCallback?.Invoke(progress);
                        }, totalDuration)
                        .ProcessAsynchronously();
                }
                finally
                {
                    // 清理临时文件
                    if (File.Exists(tempFileList))
                    {
                        File.Delete(tempFileList);
                    }
                }
            }
            else
            {
                // 使用模式匹配方式处理连续编号的文件
                var inputPattern = Path.Combine(inputDirectory, imagePattern);
                Console.WriteLine($"使用模式: {inputPattern}");
                
                // 确保输入模式路径使用正确的格式
                var normalizedPattern = inputPattern.Replace("\\", "/");
                Console.WriteLine($"标准化模式路径: {normalizedPattern}");
                
                await FFMpegArguments
                    .FromFileInput(normalizedPattern, false, options => options
                        .WithFramerate(fps))
                    .OutputToFile(outputVideoPath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .ForcePixelFormat("yuv420p")
                        .WithCustomArgument("-crf 0")  // 无损压缩
                        .WithCustomArgument("-g 18")  // GOP大小
                        .WithCustomArgument("-preset slow"))  // 高质量编码
                    .CancellableThrough(cancellationToken)
                    .NotifyOnProgress(progress => 
                    {
                        // 调试：输出原始进度值
                        Console.WriteLine($"[FFmpegService] Progress: {progress:F2}%");
                        progressCallback?.Invoke(progress);
                    }, totalDuration)
                    .ProcessAsynchronously();
            }

            return File.Exists(outputVideoPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"生成视频失败: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsFFmpegAvailableAsync()
    {
        try
        {
            // 使用 FFMpegCore 的全局配置来检查 FFmpeg 是否可用
            return await Task.FromResult(GlobalFFOptions.Current.BinaryFolder != null);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 自动检测目录中的图片文件
    /// </summary>
    /// <param name="directory">目录路径</param>
    /// <returns>图片文件列表和模式</returns>
    private static (string[] files, string pattern) DetectImageFiles(string directory)
    {
        Console.WriteLine($"[FFmpegService] 检测目录: {directory}");
        
        // 首先尝试检测所有支持的图片文件
        var allImageFiles = new List<string>();
        foreach (var extension in SupportedImageExtensions)
        {
            var files = Directory.GetFiles(directory, extension, SearchOption.TopDirectoryOnly);
            allImageFiles.AddRange(files);
            Console.WriteLine($"[FFmpegService] 找到 {extension} 文件: {files.Length} 个");
        }
        
        if (allImageFiles.Count == 0)
        {
            Console.WriteLine("[FFmpegService] 未找到任何图片文件");
            return (Array.Empty<string>(), string.Empty);
        }
        
        // 按文件名排序
        var sortedFiles = allImageFiles
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
            
        Console.WriteLine($"[FFmpegService] 总共找到 {sortedFiles.Length} 个图片文件");
        Console.WriteLine($"[FFmpegService] 前几个文件: {string.Join(", ", sortedFiles.Take(3).Select(Path.GetFileName))}");
        
        // 获取第一个文件的扩展名
        var firstFile = sortedFiles[0];
        var firstFileName = Path.GetFileNameWithoutExtension(firstFile);
        var fileExtension = Path.GetExtension(firstFile).ToLowerInvariant();
        
        Console.WriteLine($"[FFmpegService] 第一个文件: {firstFileName}, 扩展名: {fileExtension}");
        
        // 检查文件编号是否连续
        if (IsSequentialNumbering(sortedFiles))
        {
            // 如果编号连续，尝试生成模式
            var pattern = GeneratePattern(firstFileName, fileExtension);
            if (!string.IsNullOrEmpty(pattern))
            {
                Console.WriteLine($"[FFmpegService] 使用模式匹配: {pattern}");
                return (sortedFiles, pattern);
            }
        }
        
        // 如果无法使用模式匹配，使用文件列表方式
        Console.WriteLine("[FFmpegService] 使用文件列表方式");
        return (sortedFiles, "FILE_LIST");
    }
    
    /// <summary>
    /// 检查文件编号是否连续
    /// </summary>
    private static bool IsSequentialNumbering(string[] files)
    {
        if (files.Length <= 1) return true;
        
        var numbers = new List<int>();
        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var match = Regex.Match(fileName, @"(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
            {
                numbers.Add(number);
            }
        }
        
        if (numbers.Count != files.Length) return false;
        
        // 检查是否连续
        numbers.Sort();
        for (var i = 1; i < numbers.Count; i++)
        {
            if (numbers[i] != numbers[i - 1] + 1)
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// 生成FFmpeg模式
    /// </summary>
    private static string GeneratePattern(string fileName, string extension)
    {
        // 查找文件名中的数字部分
        var match = Regex.Match(fileName, @"(\D*)(\d+)(\D*)");
        if (match.Success)
        {
            var prefix = match.Groups[1].Value;
            var number = match.Groups[2].Value;
            var suffix = match.Groups[3].Value;
            
            // 根据数字位数生成模式
            var digitCount = number.Length;
            var pattern = $"{prefix}%0{digitCount}d{suffix}{extension}";
            
            Console.WriteLine($"[FFmpegService] 生成模式: 前缀='{prefix}', 数字位数={digitCount}, 后缀='{suffix}', 扩展名='{extension}'");
            return pattern;
        }
        
        return string.Empty;
    }
}
