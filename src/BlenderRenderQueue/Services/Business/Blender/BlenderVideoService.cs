using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BlenderRenderQueue.Extensions;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Business.Blender.BlenderProcess;
using BlenderRenderQueue.Services.Business.Blender.ProcessOutputParser;

namespace BlenderRenderQueue.Services.Business.Blender;

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
            var newFrame = int.Parse(writeMatch.Groups[1].Value);
            var newWidth = int.Parse(writeMatch.Groups[2].Value);
            var newHeight = int.Parse(writeMatch.Groups[3].Value);
            
            ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 解析到写入帧: {newFrame} ({newWidth}x{newHeight})", "VideoRenderOutputParser");
            
            CurrentFrame = newFrame;
            Width = newWidth;
            Height = newHeight;
            return;
        }

        // 解析视频追加帧信息
        var appendMatch = VideoAppendFrameRegex.Match(line);
        if (appendMatch.Success)
        {
            var newFrame = int.Parse(appendMatch.Groups[1].Value);
            ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 解析到追加帧: {newFrame}", "VideoRenderOutputParser");
            CurrentFrame = newFrame;
            return;
        }

        // 检测FFmpeg关闭
        if (FFmpegClosingRegex.IsMatch(line) || FFmpegFlushRegex.IsMatch(line))
        {
            ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 检测到FFmpeg关闭信号", "VideoRenderOutputParser");
            IsCompleted = true;
        }
    }

    public double GetProgress()
    {
        if (TotalFrames == 0) 
        {
            ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 获取进度: 0% (总帧数为0)", "VideoRenderOutputParser");
            return 0;
        }
        var progress = Math.Min(100, (double)CurrentFrame / TotalFrames * 100);
        ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 获取进度: {progress:F1}% (当前帧: {CurrentFrame}/{TotalFrames})", "VideoRenderOutputParser");
        return progress;
    }

    public void SetTotalFrames(int totalFrames)
    {
        ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 设置总帧数: {totalFrames}", "VideoRenderOutputParser");
        TotalFrames = totalFrames;
    }

    public void Reset()
    {
        ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 重置解析器状态", "VideoRenderOutputParser");
        CurrentFrame = 0;
        TotalFrames = 0;
        Width = 0;
        Height = 0;
        IsCompleted = false;
    }
}

/// <summary>
/// Blender video generation service implementation
/// </summary>
public class BlenderVideoService : IBlenderVideoService
{
    private static readonly string[] SupportedImageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tiff", "*.tga" };
    private readonly IBlenderProcess _blenderProcess;
    private readonly IRenderLogService? _logService;

    public BlenderVideoService(IBlenderProcess blenderProcess, IRenderLogService? logService = null)
    {
        _blenderProcess = blenderProcess;
        _logService = logService;
    }

    public async Task<bool> GenerateVideoFromImagesAsync(
        string inputDirectory,
        string outputVideoPath,
        double fps,
        string videoCodec = "H264",
        string videoQuality = "LOSSLESS",
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

        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Video, $"找到 {imageFiles.Length} 个图片文件", source: "BlenderVideoService");

        // 获取第一张图片的分辨率
        var (width, height) = GetImageDimensions(imageFiles[0]);

            // 确保输出目录存在
            var outputDir = Path.GetDirectoryName(outputVideoPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 生成Python脚本
            var pythonScript = GenerateVideoScript(imageFiles, outputVideoPath, fps, videoCodec, videoQuality, width, height);
            
            // 创建临时脚本文件
            var tempScriptPath = Path.GetTempFileName() + ".py";
            await File.WriteAllTextAsync(tempScriptPath, pythonScript, Encoding.UTF8, cancellationToken);
            
            try
            {
                // 读取Python脚本内容
                var scriptContent = await File.ReadAllTextAsync(tempScriptPath, cancellationToken);
                
                _logService?.Write(RenderLogLevel.Info, RenderLogScope.Video, $"开始生成视频: {Path.GetFileName(outputVideoPath)}", source: "BlenderVideoService");
                
                // 创建输出解析器来跟踪进度
                var outputParser = new RenderOutputParser();
                var videoParser = new VideoRenderOutputParser();
                var totalFrames = imageFiles.Length;
                videoParser.SetTotalFrames(totalFrames);
                var currentFrame = 0;
                var startTime = DateTime.Now;
                var lastReportedProgress = 0.0; // 跟踪上次报告的进度，用于检测异常跳动
                var isVideoGenerationCompleted = false; // 标记视频生成是否已完成
                
                // 创建进度更新辅助方法
                void UpdateProgress(double newProgress, string source)
                {
                    // 如果视频生成已完成，停止更新进度
                    if (isVideoGenerationCompleted)
                    {
                        return;
                    }
                    
                    // 检测异常进度跳动（进度突然大幅下降）
                    if (newProgress < lastReportedProgress - 10 && lastReportedProgress > 50)
                    {
                        _logService?.Write(RenderLogLevel.Warning, RenderLogScope.Video, $"[WARNING] 检测到异常进度跳动: {lastReportedProgress:F1}% -> {newProgress:F1}% (来源: {source})", source: "BlenderVideoService");
                    }
                    
                    _logService?.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 进度更新: {newProgress:F1}% (来源: {source}, 上次: {lastReportedProgress:F1}%)", source: "BlenderVideoService");
                    lastReportedProgress = newProgress;
                    progressCallback?.Invoke(newProgress);
                }
                
                // 启动进度跟踪任务
                var progressTask = Task.Run(async () =>
                {
                    var progress = 0.0;
                    while (progress < 95 && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(2000, cancellationToken); // 每2秒更新一次进度
                        
                        // 基于时间的进度估算（假设视频生成需要30-60秒）
                        var elapsed = DateTime.Now - startTime;
                        progress = Math.Min(95, (elapsed.TotalSeconds / 45) * 100); // 假设45秒完成
                        UpdateProgress(progress, "时间基础进度");
                    }
                }, cancellationToken);
                progressTask.FireAndForget(
                    source: nameof(BlenderVideoService),
                    message: "视频生成进度后台任务失败。");
                
                // 订阅Blender进程的输出事件
                _blenderProcess.OnOutputReceived += (line) =>
                {
                    _logService?.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 收到输出: {line.Trim()}", source: "BlenderVideoService");
                    
                    // 使用视频解析器解析输出
                    videoParser.ParseLine(line);
                    
                    // 基于视频解析器结果更新进度
                    if (videoParser.CurrentFrame > 0)
                    {
                        var progress = videoParser.GetProgress();
                        UpdateProgress(progress, "视频解析器");
                    }
                    
                    // 基于输出内容更新进度
                    if (line.Contains("开始渲染视频"))
                    {
                        UpdateProgress(5, "开始渲染视频");
                    }
                    else if (line.Contains("Rendering animation"))
                    {
                        UpdateProgress(10, "Rendering animation");
                    }
                    else if (line.Contains("视频渲染完成"))
                    {
                        UpdateProgress(95, "视频渲染完成");
                    }
                    else if (line.Contains("输出文件已生成"))
                    {
                        UpdateProgress(100, "输出文件已生成");
                    }
                    else if (line.Contains("渲染失败") || line.Contains("错误"))
                    {
                        _logService?.Write(RenderLogLevel.Error, RenderLogScope.Video, $"[ERROR] 检测到错误: {line}", source: "BlenderVideoService");
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
                                    UpdateProgress(overallProgress * 100, $"渲染进度事件(帧:{currentFrame}/{totalFrames})");
                                }
                                break;
                            case RenderCompletedFrame completedFrame:
                                currentFrame++;
                                // 更新进度
                                var progress = (double)currentFrame / totalFrames;
                                UpdateProgress(progress * 100, $"完成帧事件(帧:{currentFrame}/{totalFrames})");
                                break;
                            case RenderCompletedAll:
                                UpdateProgress(100, "渲染全部完成事件");
                                break;
                            case RenderError error:
                                _logService?.Write(RenderLogLevel.Error, RenderLogScope.Video, $"[ERROR] 渲染错误: {error.Message}", source: "BlenderVideoService");
                                break;
                        }
                    }
                };
                
                _blenderProcess.OnErrorReceived += (line) =>
                {
                    _logService?.Write(RenderLogLevel.Error, RenderLogScope.Video, $"[ERROR] 收到错误输出: {line}", source: "BlenderVideoService");
                };
                
                // 使用Blender执行脚本
                _logService?.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] 开始执行Blender脚本", source: "BlenderVideoService");
                var result = await _blenderProcess.ExecuteScriptAsync(
                    scriptContent,
                    cancellationToken);

                _logService?.Write(RenderLogLevel.Debug, RenderLogScope.Video, $"[DEBUG] Blender脚本执行完成", source: "BlenderVideoService");
                
                if (!string.IsNullOrEmpty(result))
                {
                    if (File.Exists(outputVideoPath))
                    {
                        var fileInfo = new FileInfo(outputVideoPath);
                        _logService?.Write(RenderLogLevel.Info, RenderLogScope.Video, $"✅ 视频生成成功: {Path.GetFileName(outputVideoPath)} ({fileInfo.Length / 1024 / 1024} MB)", source: "BlenderVideoService");
                        
                        // 标记视频生成已完成
                        isVideoGenerationCompleted = true;
                        
                        // 只有在确认视频文件存在且生成完成后才设置进度为100%
                        UpdateProgress(100, "视频生成完成");
                        return true;
                    }
                    else
                    {
                        _logService?.Write(RenderLogLevel.Error, RenderLogScope.Video, $"❌ 输出文件不存在: {Path.GetFileName(outputVideoPath)}", source: "BlenderVideoService");
                        isVideoGenerationCompleted = true;
                        return false;
                    }
                }
                else
                {
                    _logService?.Write(RenderLogLevel.Error, RenderLogScope.Video, $"❌ 视频生成失败", source: "BlenderVideoService");
                    isVideoGenerationCompleted = true;
                    return false;
                }
            }
            finally
            {
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
            if (string.IsNullOrEmpty(_blenderProcess.BlenderPath) || !File.Exists(_blenderProcess.BlenderPath))
            {
                return false;
            }

            var result = await _blenderProcess.ExecuteScriptAsync(
                "print('Blender is available')",
                CancellationToken.None);

            return !string.IsNullOrEmpty(result);
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
            var result = await _blenderProcess.ExecuteScriptAsync(
                "print(bpy.app.version_string)",
                CancellationToken.None);

            if (string.IsNullOrEmpty(result)) return "Unknown";
            var lines = result.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(".") && !line.Contains("Blender"))
                {
                    return line.Trim();
                }
            }

            return "Unknown";
        }
        catch (Exception ex)
        {
            _logService?.Write(RenderLogLevel.Error, RenderLogScope.Video, $"获取Blender版本信息失败: {ex.Message}", source: "BlenderVideoService");
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
            
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Video, $"成功获取图片分辨率: {width}x{height} ({Path.GetFileName(imagePath)})", nameof(BlenderVideoService));
            return (width, height);
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.Video, $"获取图片分辨率失败: {ex.Message}", nameof(BlenderVideoService));
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Video, "使用默认分辨率: 1920x1080", nameof(BlenderVideoService));
            return (1920, 1080); // 默认分辨率
        }
    }


    /// <summary>
    /// 生成Blender视频脚本
    /// </summary>
    private static string GenerateVideoScript(string[] imageFiles, string outputVideoPath, double fps, string videoCodec, string videoQuality, int width, int height)
    {
        var script = new StringBuilder();
        
        // 导入必要的模块
        script.AppendLine("import bpy");
        script.AppendLine("import os");
        script.AppendLine("import sys");
        script.AppendLine("import traceback");
        script.AppendLine();

        // 创建全新的空场景
        script.AppendLine("# 创建全新的空场景");
        script.AppendLine("bpy.ops.wm.read_homefile(app_template='', use_empty=True)");
        script.AppendLine("print('已创建全新的空场景')");
        script.AppendLine();
        
        // 空场景已经创建，无需额外清理
        script.AppendLine("# 空场景已创建，无需额外清理");
        script.AppendLine();

        // 设置场景属性（在添加strip之前设置）
        script.AppendLine($"bpy.context.scene.frame_start = 0");
        script.AppendLine($"bpy.context.scene.frame_end = {imageFiles.Length - 1}");
        script.AppendLine($"bpy.context.scene.render.fps = {fps}");
        script.AppendLine();
        
        // 设置渲染分辨率（在添加strip之前设置）
        script.AppendLine($"bpy.context.scene.render.resolution_x = {width}");
        script.AppendLine($"bpy.context.scene.render.resolution_y = {height}");
        script.AppendLine($"bpy.context.scene.render.resolution_percentage = 100");
        script.AppendLine();

        // 确保有序列编辑器
        script.AppendLine("if not bpy.context.scene.sequence_editor:");
        script.AppendLine("    bpy.context.scene.sequence_editor_create()");
        script.AppendLine();

        // 添加图片序列到时间轴（在设置分辨率之后）
        script.AppendLine("# 添加图片序列到VSE");
        for (var i = 0; i < imageFiles.Length; i++)
        {
            var imagePath = imageFiles[i].Replace("\\", "/");
            const int channel = 1;
            
            script.AppendLine($"bpy.context.scene.sequence_editor.strips.new_image(");
            script.AppendLine($"    name='{Path.GetFileNameWithoutExtension(imagePath)}',");
            script.AppendLine($"    filepath='{imagePath}',");
            script.AppendLine($"    channel={channel},");
            script.AppendLine($"    frame_start={i}");
            script.AppendLine(")");
        }
        script.AppendLine();

        // 设置色彩空间
        script.AppendLine("bpy.context.scene.view_settings.view_transform = 'Standard'");
        script.AppendLine("bpy.context.scene.view_settings.look = 'None'");
        script.AppendLine();

        // 设置输出格式
        script.AppendLine("if bpy.app.version >= (5, 0, 0):");
        script.AppendLine("    bpy.context.scene.render.image_settings.media_type = 'VIDEO'");
        script.AppendLine("else:");
        script.AppendLine("    bpy.context.scene.render.image_settings.file_format = 'FFMPEG'");
        script.AppendLine();

        // 设置FFmpeg编码
        script.AppendLine($"bpy.context.scene.render.ffmpeg.format = 'MPEG4'");
        script.AppendLine($"bpy.context.scene.render.ffmpeg.codec = '{videoCodec}'");
        script.AppendLine($"bpy.context.scene.render.ffmpeg.constant_rate_factor = '{videoQuality}'");
        script.AppendLine("bpy.context.scene.render.image_settings.color_mode = 'RGB'");
        script.AppendLine();

        // 强制设置渲染引擎为Workbench - 多次设置确保生效
        script.AppendLine("# 强制设置渲染引擎为BLENDER_WORKBENCH");
        script.AppendLine("bpy.context.scene.render.engine = 'BLENDER_WORKBENCH'");
        script.AppendLine();
        
        // 设置Workbench引擎的特定参数
        script.AppendLine("# 设置Workbench引擎参数");
        script.AppendLine("if hasattr(bpy.context.scene, 'display'):");
        script.AppendLine("    bpy.context.scene.display.shading.light = 'FLAT'");
        script.AppendLine("    bpy.context.scene.display.shading.color_type = 'TEXTURE'");
        script.AppendLine("    bpy.context.scene.display.shading.show_xray = False");
        script.AppendLine();
        
        // 确保视图设置正确
        script.AppendLine("bpy.context.scene.view_settings.view_transform = 'Standard'");
        script.AppendLine("bpy.context.scene.view_settings.look = 'None'");
        script.AppendLine();
        
        // 再次确认引擎设置
        script.AppendLine("bpy.context.scene.render.engine = 'BLENDER_WORKBENCH'");
        script.AppendLine();

        // 设置输出路径
        script.AppendLine($"bpy.context.scene.render.filepath = '{outputVideoPath.Replace("\\", "/")}'");
        script.AppendLine();

        // VSE渲染不需要3D相机
        script.AppendLine("# VSE渲染不需要3D相机");
        script.AppendLine();

        script.AppendLine($"print('开始渲染视频: {outputVideoPath.Replace("\\", "/")}')");
        script.AppendLine("print(f'Engine: {bpy.context.scene.render.engine}')");
        script.AppendLine($"print('Rendering animation (frames 0..{imageFiles.Length - 1})')");
        script.AppendLine("print('Start rendering: Scene, ViewLayer')");
        script.AppendLine();
        
        // 验证场景状态
        script.AppendLine("print(f'场景对象数量: {len(bpy.context.scene.objects)}')");
        script.AppendLine("print(f'场景材质数量: {len(bpy.data.materials)}')");
        script.AppendLine("print(f'场景纹理数量: {len(bpy.data.textures)}')");
        script.AppendLine("print(f'场景网格数量: {len(bpy.data.meshes)}')");
        script.AppendLine("print(f'场景灯光数量: {len(bpy.data.lights)}')");
        script.AppendLine();
        
        // 验证渲染引擎设置
        script.AppendLine("if bpy.context.scene.render.engine != 'BLENDER_WORKBENCH':");
        script.AppendLine("    print(f'警告: 渲染引擎不是BLENDER_WORKBENCH，当前是: {bpy.context.scene.render.engine}')");
        script.AppendLine("    bpy.context.scene.render.engine = 'BLENDER_WORKBENCH'");
        script.AppendLine("    print('已强制设置为BLENDER_WORKBENCH')");
        script.AppendLine();
        
        // 渲染前最后一次确认引擎设置
        script.AppendLine("print(f'最终渲染引擎: {bpy.context.scene.render.engine}')");
        script.AppendLine();
        
        script.AppendLine("try:");
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
