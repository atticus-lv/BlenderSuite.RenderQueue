using System;
using System.Diagnostics;
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
                    var fileListContent = imageFiles.Select(f => $"file '{f.Replace("\\", "/")}'").ToList();
                    fileListContent.Add(""); // 添加空行
                    await File.WriteAllLinesAsync(tempFileList, fileListContent, cancellationToken);
                    
                    // Console.WriteLine($"临时文件列表内容:\n{string.Join("\n", fileListContent)}");
                    
                    // 使用 concat demuxer 和正确的参数
                    await FFMpegArguments
                        .FromFileInput(tempFileList, false, options => options
                            .WithFramerate(fps)
                            .WithCustomArgument("-f concat -safe 0"))
                        .OutputToFile(outputVideoPath, true, options => options
                            .WithVideoCodec(VideoCodec.LibX265)
                            .WithVideoBitrate(20000))
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
                
                await FFMpegArguments
                    .FromFileInput(inputPattern, false, options => options
                        .WithFramerate(fps))
                    .OutputToFile(outputVideoPath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX265)
                        .WithVideoBitrate(20000))
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
        foreach (var extension in SupportedImageExtensions)
        {
            var files = Directory.GetFiles(directory, extension, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length > 0)
            {
                // 检查文件命名格式
                var firstFile = Path.GetFileNameWithoutExtension(files[0]);
                var ext = extension.Substring(1); // 移除 "*"
                
                // 如果文件名包含数字，分析数字格式
                if (Regex.IsMatch(firstFile, @"\d+"))
                {
                    // 检查数字的位数和格式
                    var match = Regex.Match(firstFile, @"(\D*)(\d+)(\D*)");
                    if (match.Success)
                    {
                        var prefix = match.Groups[1].Value;
                        var number = match.Groups[2].Value;
                        var suffix = match.Groups[3].Value;
                        
                        // 根据数字位数生成模式
                        var digitCount = number.Length;
                        var pattern = $"{prefix}%0{digitCount}d{suffix}{ext}";
                        
                        // 检查文件编号是否连续从0开始
                        var firstNumber = int.Parse(number);
                        if (firstNumber == 0)
                        {
                            // 如果从0开始，直接使用模式
                            return (files, pattern);
                        }
                        else
                        {
                            // 如果不从0开始，需要重命名文件或使用不同的方法
                            // 这里我们使用文件列表的方式而不是模式匹配
                            return (files, "FILE_LIST");
                        }
                    }
                }
                
                // 默认使用简单的 %d 模式
                var defaultPattern = $"%d{ext}";
                return (files, defaultPattern);
            }
        }

        return (Array.Empty<string>(), string.Empty);
    }
}
