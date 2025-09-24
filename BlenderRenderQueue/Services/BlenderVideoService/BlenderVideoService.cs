using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.BlenderService;

namespace BlenderRenderQueue.Services.BlenderVideoService;

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
            Console.WriteLine($"[BlenderVideoService] 前几个文件: {string.Join(", ", imageFiles.Take(3).Select(Path.GetFileName))}");

            // 确保输出目录存在
            var outputDir = Path.GetDirectoryName(outputVideoPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 生成Python脚本
            var pythonScript = GenerateVideoScript(imageFiles, outputVideoPath, fps, videoCodec);
            
            // 创建临时脚本文件
            var tempScriptPath = Path.GetTempFileName() + ".py";
            await File.WriteAllTextAsync(tempScriptPath, pythonScript, Encoding.UTF8, cancellationToken);
            
            Console.WriteLine($"[BlenderVideoService] 生成Python脚本: {tempScriptPath}");
            Console.WriteLine($"[BlenderVideoService] 输出视频: {outputVideoPath}");

            try
            {
                // 读取Python脚本内容
                var scriptContent = await File.ReadAllTextAsync(tempScriptPath, cancellationToken);
                
                // 使用Blender执行脚本
                var result = await _blenderService.ExecuteScript(
                    scriptContent,
                    "generate_video",
                    cancellationToken);

                return result.ExitCode == 0 && File.Exists(outputVideoPath);
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
    /// 生成Blender视频脚本
    /// </summary>
    private static string GenerateVideoScript(string[] imageFiles, string outputVideoPath, double fps, string videoCodec)
    {
        var script = new StringBuilder();
        
        // 导入必要的模块
        script.AppendLine("import bpy");
        script.AppendLine("import os");
        script.AppendLine("import sys");
        script.AppendLine();

        // 清理场景
        script.AppendLine("# 清理场景");
        script.AppendLine("bpy.ops.object.select_all(action='SELECT')");
        script.AppendLine("bpy.ops.object.delete(use_global=False)");
        script.AppendLine();

        // 确保有序列编辑器
        script.AppendLine("# 确保有序列编辑器");
        script.AppendLine("if not bpy.context.scene.sequence_editor:");
        script.AppendLine("    bpy.context.scene.sequence_editor_create()");
        script.AppendLine();

        // 添加图片序列到时间轴
        script.AppendLine("# 添加图片序列到时间轴");
        for (int i = 0; i < imageFiles.Length; i++)
        {
            var imagePath = imageFiles[i].Replace("\\", "/");
            var frameStart = i;
            var channel = 1;
            
            script.AppendLine($"# 添加图片 {i + 1}/{imageFiles.Length}: {Path.GetFileName(imagePath)}");
            script.AppendLine($"bpy.context.scene.sequence_editor.strips.new_image(");
            script.AppendLine($"    name='{Path.GetFileNameWithoutExtension(imagePath)}',");
            script.AppendLine($"    filepath='{imagePath}',");
            script.AppendLine($"    channel={channel},");
            script.AppendLine($"    frame_start={frameStart}");
            script.AppendLine(")");
            script.AppendLine();
        }

        // 设置场景属性
        script.AppendLine("# 设置场景属性");
        script.AppendLine($"bpy.context.scene.frame_start = 0");
        script.AppendLine($"bpy.context.scene.frame_end = {imageFiles.Length - 1}");
        script.AppendLine($"bpy.context.scene.render.fps = {fps}");
        script.AppendLine();

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
        script.AppendLine("print(f'开始渲染视频: {outputVideoPath}')");
        script.AppendLine("bpy.ops.render.render('INVOKE_DEFAULT', animation=True, use_viewport=True)");
        script.AppendLine("print('视频渲染完成')");

        return script.ToString();
    }
}
