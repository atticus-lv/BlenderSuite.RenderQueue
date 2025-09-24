using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BlenderRenderQueue.Services.BlenderService;
using BlenderRenderQueue.Services.BlenderService.ServiceOutputParser;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.BlenderVideoService;

/// <summary>
/// 视频渲染输出解析器
/// </summary>
public class VideoRenderOutputParser
{
    private static readonly Regex VideoWriteFrameRegex = new(@"ffmpeg: writing frame #(\d+) \((\d+)x(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex VideoAppendFrameRegex = new(@"Video append frame (\d+)", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"Time: (\d{2}:\d{2}\.\d{2})", RegexOptions.Compiled);
    private static readonly Regex ExecutingSequencerRegex = new(@"Executing sequencer", RegexOptions.Compiled);
    private static readonly Regex FFmpegClosingRegex = new(@"ffmpeg: closing", RegexOptions.Compiled);
    private static readonly Regex FFmpegFlushRegex = new(@"ffmpeg: flush delayed video frames", RegexOptions.Compiled);

    public int CurrentFrame { get; private set; }
    public int TotalFrames { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsCompleted { get; private set; }

    public void ParseLine(string line)
    {
        // 解析视频写入帧信息
        var writeMatch = VideoWriteFrameRegex.Match(line);
        if (writeMatch.Success)
        {
            CurrentFrame = int.Parse(writeMatch.Groups[1].Value);
            Width = int.Parse(writeMatch.Groups[2].Value);
            Height = int.Parse(writeMatch.Groups[3].Value);
            return;
        }

        // 解析视频追加帧信息
        var appendMatch = VideoAppendFrameRegex.Match(line);
        if (appendMatch.Success)
        {
            CurrentFrame = int.Parse(appendMatch.Groups[1].Value);
            return;
        }

        // 检测FFmpeg关闭
        if (FFmpegClosingRegex.IsMatch(line) || FFmpegFlushRegex.IsMatch(line))
        {
            IsCompleted = true;
        }
    }

    public double GetProgress()
    {
        if (TotalFrames == 0) return 0;
        return Math.Min(100, (double)CurrentFrame / TotalFrames * 100);
    }

    public void SetTotalFrames(int totalFrames)
    {
        TotalFrames = totalFrames;
    }

    public void Reset()
    {
        CurrentFrame = 0;
        TotalFrames = 0;
        Width = 0;
        Height = 0;
        IsCompleted = false;
    }
}

/// <summary>
/// Blender视频生成服务实现
/// </summary>
public class BlenderVideoService : IBlenderVideoService
{
    private static readonly string[] SupportedImageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tiff", "*.tga" };
    private readonly BlenderExeService _blenderService;

    public BlenderVideoService(BlenderExeService blenderService)
    {
        _blenderService = blenderService;
    }

    public async Task<bool> GenerateVideoFromImagesAsync(
        string inputDirectory,
        string outputVideoPath,
        double fps,
        string videoCodec = "H264",
        Action<double>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(inputDirectory))
            {
                throw new DirectoryNotFoundException($"输入目录不存在: {inputDirectory}");
            }

            // 检测图片文件
            var imageFiles = DetectImageFiles(inputDirectory);
            if (imageFiles.Length == 0)
            {
                throw new InvalidOperationException($"在目录 {inputDirectory} 中未找到支持的图片文件 (PNG, JPG, JPEG, BMP, TIFF, TGA)");
            }

        Console.WriteLine($"[BlenderVideoService] 找到 {imageFiles.Length} 个图片文件");

        // 获取第一张图片的分辨率
        var (width, height) = GetImageDimensions(imageFiles[0]);

            // 确保输出目录存在
            var outputDir = Path.GetDirectoryName(outputVideoPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 生成Python脚本
            var pythonScript = GenerateVideoScript(imageFiles, outputVideoPath, fps, videoCodec, width, height);
            
            // 创建临时脚本文件
            var tempScriptPath = Path.GetTempFileName() + ".py";
            await File.WriteAllTextAsync(tempScriptPath, pythonScript, Encoding.UTF8, cancellationToken);
            
            try
            {
                // 读取Python脚本内容
                var scriptContent = await File.ReadAllTextAsync(tempScriptPath, cancellationToken);
                
                Console.WriteLine($"[BlenderVideoService] 开始生成视频: {Path.GetFileName(outputVideoPath)}");
                
                // 创建输出解析器来跟踪进度
                var outputParser = new RenderOutputParser();
                var videoParser = new VideoRenderOutputParser();
                var totalFrames = imageFiles.Length;
                videoParser.SetTotalFrames(totalFrames);
                var currentFrame = 0;
                var startTime = DateTime.Now;
                
                // 启动进度跟踪任务
                var progressTask = Task.Run(async () =>
                {
                    var progress = 0.0;
                    while (progress < 95)
                    {
                        await Task.Delay(2000); // 每2秒更新一次进度
                        
                        // 基于时间的进度估算（假设视频生成需要30-60秒）
                        var elapsed = DateTime.Now - startTime;
                        progress = Math.Min(95, (elapsed.TotalSeconds / 45) * 100); // 假设45秒完成
                        progressCallback?.Invoke(progress);
                    }
                });
                
                // 订阅Blender服务的输出事件
                _blenderService.OnOutputReceived += (line) =>
                {
                    // 使用视频解析器解析输出
                    videoParser.ParseLine(line);
                    
                    // 基于视频解析器结果更新进度
                    if (videoParser.CurrentFrame > 0)
                    {
                        var progress = videoParser.GetProgress();
                        progressCallback?.Invoke(progress);
                    }
                    
                    // 基于输出内容更新进度
                    if (line.Contains("开始渲染视频"))
                    {
                        progressCallback?.Invoke(5);
                    }
                    else if (line.Contains("Rendering animation"))
                    {
                        progressCallback?.Invoke(10);
                    }
                    else if (line.Contains("视频渲染完成"))
                    {
                        progressCallback?.Invoke(95);
                    }
                    else if (line.Contains("输出文件已生成"))
                    {
                        progressCallback?.Invoke(100);
                    }
                    else if (line.Contains("渲染失败") || line.Contains("错误"))
                    {
                        Console.WriteLine($"[BlenderVideoService] [ERROR] 检测到错误: {line}");
                    }
                    
                    // 解析输出以获取进度信息
                    var events = outputParser.ParseLine(line);
                    foreach (var evt in events)
                    {
                        switch (evt)
                        {
                            case RenderProgressEvent progressEvent:
                                if (progressEvent.Progress.SampleCurrent.HasValue && progressEvent.Progress.SampleTotal.HasValue)
                                {
                                    // 计算当前帧的进度
                                    var frameProgress = (double)progressEvent.Progress.SampleCurrent.Value / progressEvent.Progress.SampleTotal.Value;
                                    // 计算总体进度
                                    var overallProgress = (currentFrame + frameProgress) / totalFrames;
                                    progressCallback?.Invoke(overallProgress * 100);
                                }
                                break;
                            case RenderCompletedFrame completedFrame:
                                currentFrame++;
                                // 更新进度
                                var progress = (double)currentFrame / totalFrames;
                                progressCallback?.Invoke(progress * 100);
                                break;
                            case RenderCompletedAll:
                                progressCallback?.Invoke(100);
                                break;
                            case RenderError error:
                                Console.WriteLine($"[BlenderVideoService] [ERROR] {error.Message}");
                                break;
                        }
                    }
                };
                
                _blenderService.OnErrorReceived += (line) =>
                {
                    Console.WriteLine($"[BlenderVideoService] [ERROR] {line}");
                };
                
                // 使用Blender执行脚本
                var result = await _blenderService.ExecuteScript(
                    scriptContent,
                    "generate_video",
                    cancellationToken);

                // 确保进度达到100%
                progressCallback?.Invoke(100);
                
                if (result.ExitCode == 0)
                {
                    if (File.Exists(outputVideoPath))
                    {
                        var fileInfo = new FileInfo(outputVideoPath);
                        Console.WriteLine($"[BlenderVideoService] ✅ 视频生成成功: {Path.GetFileName(outputVideoPath)} ({fileInfo.Length / 1024 / 1024} MB)");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"[BlenderVideoService] ❌ 输出文件不存在: {Path.GetFileName(outputVideoPath)}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine($"[BlenderVideoService] ❌ 视频生成失败，退出码: {result.ExitCode}");
                    return false;
                }
            }
            finally
            {
                // 清理临时脚本文件
                if (File.Exists(tempScriptPath))
                {
                    File.Delete(tempScriptPath);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"使用Blender生成视频失败: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsBlenderAvailableAsync()
    {
        try
        {
            // 检查Blender路径是否存在
            if (string.IsNullOrEmpty(_blenderService.BlenderPath) || !File.Exists(_blenderService.BlenderPath))
            {
                return false;
            }

            // 尝试执行一个简单的Blender命令来验证可用性
            var result = await _blenderService.ExecuteScript(
                "print('Blender is available')",
                "check_availability",
                CancellationToken.None);

            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetBlenderVersionAsync()
    {
        try
        {
            var result = await _blenderService.ExecuteScript(
                "print(bpy.app.version_string)",
                "get_version",
                CancellationToken.None);

            if (result.ExitCode == 0 && !string.IsNullOrEmpty(result.Output))
            {
                // 提取版本信息
                var lines = result.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains(".") && !line.Contains("Blender"))
                    {
                        return line.Trim();
                    }
                }
            }

            return "Unknown";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlenderVideoService] 获取Blender版本信息失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 检测目录中的图片文件
    /// </summary>
    private static string[] DetectImageFiles(string directory)
    {
        var allImageFiles = new List<string>();
        foreach (var extension in SupportedImageExtensions)
        {
            var files = Directory.GetFiles(directory, extension, SearchOption.TopDirectoryOnly);
            allImageFiles.AddRange(files);
        }

        return allImageFiles
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 获取图片的分辨率（使用Avalonia Bitmap）
    /// </summary>
    private static (int width, int height) GetImageDimensions(string imagePath)
    {
        try
        {
            using var fileStream = File.OpenRead(imagePath);
            using var bitmap = new Bitmap(fileStream);
            
            var width = bitmap.PixelSize.Width;
            var height = bitmap.PixelSize.Height;
            
            Console.WriteLine($"[BlenderVideoService] 成功获取图片分辨率: {width}x{height} ({Path.GetFileName(imagePath)})");
            return (width, height);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BlenderVideoService] 获取图片分辨率失败: {ex.Message}");
            Console.WriteLine($"[BlenderVideoService] 使用默认分辨率: 1920x1080");
            return (1920, 1080); // 默认分辨率
        }
    }


    /// <summary>
    /// 生成Blender视频脚本
    /// </summary>
    private static string GenerateVideoScript(string[] imageFiles, string outputVideoPath, double fps, string videoCodec, int width, int height)
    {
        var script = new StringBuilder();
        
        // 导入必要的模块
        script.AppendLine("import bpy");
        script.AppendLine("import os");
        script.AppendLine("import sys");
        script.AppendLine("import traceback");
        script.AppendLine();

        // 清理场景
        script.AppendLine("# 清理场景");
        script.AppendLine("bpy.ops.object.select_all(action='SELECT')");
        script.AppendLine("bpy.ops.object.delete(use_global=False)");
        script.AppendLine();

        // 设置场景属性（在添加strip之前设置）
        script.AppendLine("# 设置场景属性");
        script.AppendLine($"bpy.context.scene.frame_start = 0");
        script.AppendLine($"bpy.context.scene.frame_end = {imageFiles.Length - 1}");
        script.AppendLine($"bpy.context.scene.render.fps = {fps}");
        script.AppendLine();
        
        // 设置渲染分辨率（在添加strip之前设置）
        script.AppendLine("# 设置渲染分辨率");
        script.AppendLine($"bpy.context.scene.render.resolution_x = {width}");
        script.AppendLine($"bpy.context.scene.render.resolution_y = {height}");
        script.AppendLine($"bpy.context.scene.render.resolution_percentage = 100");
        script.AppendLine();

        // 确保有序列编辑器
        script.AppendLine("# 确保有序列编辑器");
        script.AppendLine("if not bpy.context.scene.sequence_editor:");
        script.AppendLine("    bpy.context.scene.sequence_editor_create()");
        script.AppendLine();

        // 添加图片序列到时间轴（在设置分辨率之后）
        script.AppendLine("# 添加图片序列到时间轴");
        for (int i = 0; i < imageFiles.Length; i++)
        {
            var imagePath = imageFiles[i].Replace("\\", "/");
            var frameStart = i;
            var channel = 1;
            
            script.AppendLine($"bpy.context.scene.sequence_editor.strips.new_image(");
            script.AppendLine($"    name='{Path.GetFileNameWithoutExtension(imagePath)}',");
            script.AppendLine($"    filepath='{imagePath}',");
            script.AppendLine($"    channel={channel},");
            script.AppendLine($"    frame_start={frameStart}");
            script.AppendLine(")");
        }

        // 设置色彩空间
        script.AppendLine("# 设置色彩空间");
        script.AppendLine("bpy.context.scene.view_settings.view_transform = 'Standard'");
        script.AppendLine("bpy.context.scene.view_settings.look = 'None'");
        script.AppendLine();

        // 设置输出格式
        script.AppendLine("# 设置输出格式");
        script.AppendLine("if bpy.app.version >= (5, 0, 0):");
        script.AppendLine("    bpy.context.scene.render.image_settings.media_type = 'VIDEO'");
        script.AppendLine("else:");
        script.AppendLine("    bpy.context.scene.render.image_settings.file_format = 'FFMPEG'");
        script.AppendLine();

        // 设置FFmpeg编码
        script.AppendLine("# 设置FFmpeg编码");
        script.AppendLine($"bpy.context.scene.render.ffmpeg.format = 'MPEG4'");
        script.AppendLine($"bpy.context.scene.render.ffmpeg.codec = '{videoCodec}'");
        script.AppendLine("bpy.context.scene.render.ffmpeg.constant_rate_factor = 'PERC_LOSSLESS'");
        script.AppendLine("bpy.context.scene.render.image_settings.color_mode = 'RGB'");
        script.AppendLine();

        // 设置渲染引擎
        script.AppendLine("# 设置渲染引擎");
        script.AppendLine("bpy.context.scene.render.engine = 'BLENDER_WORKBENCH'");
        script.AppendLine();

        // 设置输出路径
        script.AppendLine("# 设置输出路径");
        script.AppendLine($"bpy.context.scene.render.filepath = '{outputVideoPath.Replace("\\", "/")}'");
        script.AppendLine();

        // 确保有相机
        script.AppendLine("# 确保有相机");
        script.AppendLine("if not any(obj.type == 'CAMERA' for obj in bpy.context.scene.objects):");
        script.AppendLine("    bpy.ops.object.camera_add()");
        script.AppendLine("    camera = bpy.context.active_object");
        script.AppendLine("    bpy.context.scene.camera = camera");
        script.AppendLine();

        // 开始渲染
        script.AppendLine("# 开始渲染");
        script.AppendLine($"print('开始渲染视频: {outputVideoPath.Replace("\\", "/")}')");
        script.AppendLine("print('Engine: BLENDER_WORKBENCH')");
        script.AppendLine($"print('Rendering animation (frames 0..{imageFiles.Length - 1})')");
        script.AppendLine("print('Start rendering: Scene, ViewLayer')");
        script.AppendLine();
        script.AppendLine("# 使用序列编辑器渲染视频");
        script.AppendLine("try:");
        script.AppendLine("    # 渲染动画");
        script.AppendLine("    bpy.ops.render.render('INVOKE_DEFAULT', animation=True, use_viewport=True)");
        script.AppendLine("    print('视频渲染完成')");
        script.AppendLine($"    if os.path.exists('{outputVideoPath.Replace("\\", "/")}'):");
        script.AppendLine("        print('输出文件已生成')");
        script.AppendLine("    else:");
        script.AppendLine("        print('警告: 输出文件未找到')");
        script.AppendLine("except Exception as e:");
        script.AppendLine("    print(f'渲染失败: {str(e)}')");
        script.AppendLine("    traceback.print_exc()");
        script.AppendLine("    sys.exit(1)");

        return script.ToString();
    }
}
