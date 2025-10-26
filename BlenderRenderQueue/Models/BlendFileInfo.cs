using System;
using System.IO;
using Avalonia.Media.Imaging;
using BlenderRenderQueue.Helpers;

namespace BlenderRenderQueue.Models;

/// <summary>
/// Blender文件系统信息
/// </summary>
public class BlendFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => !string.IsNullOrEmpty(FilePath) ? Path.GetFileName(FilePath) : string.Empty;
    public string FileNameWithoutExtension => !string.IsNullOrEmpty(FilePath) ? Path.GetFileNameWithoutExtension(FilePath) : string.Empty;
    public string DirectoryPath => !string.IsNullOrEmpty(FilePath) ? Path.GetDirectoryName(FilePath) ?? string.Empty : string.Empty;
    public long FileSizeBytes { get; set; }
    public string FileSizeFormatted => FormatFileSize(FileSizeBytes);
    public DateTime CreatedTime { get; set; }
    public DateTime LastModifiedTime { get; set; }
    public Bitmap? Thumbnail { get; set; }
    public bool HasThumbnail => Thumbnail != null;
    public bool IsValid => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    /// <summary>
    /// 从文件路径加载文件信息
    /// </summary>
    public static BlendFileInfo FromFilePath(string filePath)
    {
        Console.WriteLine($"[BlendFileInfo] Starting to load file info: {filePath}");
        
        var fileInfo = new BlendFileInfo
        {
            FilePath = filePath
        };

        if (File.Exists(filePath))
        {
            var fileInfoObj = new FileInfo(filePath);
            fileInfo.FileSizeBytes = fileInfoObj.Length;
            fileInfo.CreatedTime = fileInfoObj.CreationTime;
            fileInfo.LastModifiedTime = fileInfoObj.LastWriteTime;
            
            Console.WriteLine($"[BlendFileInfo] File basic info - Size: {fileInfo.FileSizeFormatted}, Created: {fileInfo.CreatedTime:yyyy-MM-dd HH:mm:ss}, Modified: {fileInfo.LastModifiedTime:yyyy-MM-dd HH:mm:ss}");
            
            // 提取缩略图
            Console.WriteLine($"[BlendFileInfo] Starting thumbnail extraction...");
            fileInfo.Thumbnail = BlendThumbnailExtractor.ExtractThumbnailWithStatus(filePath, out var status);

            Console.WriteLine(fileInfo.Thumbnail != null
                ? $"[BlendFileInfo] ✅ Thumbnail extraction successful! Size: {fileInfo.Thumbnail.PixelSize.Width}x{fileInfo.Thumbnail.PixelSize.Height}"
                : $"[BlendFileInfo] ❌ Thumbnail extraction failed - Status: {status}");
        }
        else
        {
            Console.WriteLine($"[BlendFileInfo] ❌ File does not exist: {filePath}");
        }

        Console.WriteLine($"[BlendFileInfo] File info loading completed");
        return fileInfo;
    }

    /// <summary>
    /// 刷新文件信息（重新读取文件属性和缩略图）
    /// </summary>
    public void Refresh()
    {
        Console.WriteLine($"[BlendFileInfo] Refreshing file info: {FilePath}");
        
        if (File.Exists(FilePath))
        {
            var fileInfoObj = new FileInfo(FilePath);
            FileSizeBytes = fileInfoObj.Length;
            CreatedTime = fileInfoObj.CreationTime;
            LastModifiedTime = fileInfoObj.LastWriteTime;
            
            Console.WriteLine($"[BlendFileInfo] File info updated - Size: {FileSizeFormatted}, Modified: {LastModifiedTime:yyyy-MM-dd HH:mm:ss}");
            
            // 重新提取缩略图
            Console.WriteLine($"[BlendFileInfo] Re-extracting thumbnail...");
            Thumbnail?.Dispose();
            Thumbnail = BlendThumbnailExtractor.ExtractThumbnailWithStatus(FilePath, out var status);

            Console.WriteLine(Thumbnail != null
                ? $"[BlendFileInfo] ✅ Thumbnail refresh successful! Size: {Thumbnail.PixelSize.Width}x{Thumbnail.PixelSize.Height}"
                : $"[BlendFileInfo] ❌ Thumbnail refresh failed - Status: {status}");
        }
        else
        {
            Console.WriteLine($"[BlendFileInfo] ❌ File does not exist, cannot refresh: {FilePath}");
        }
    }

    /// <summary>
    /// 释放缩略图资源
    /// </summary>
    public void Dispose()
    {
        Console.WriteLine($"[BlendFileInfo] Disposing resources: {FilePath}");
        
        if (Thumbnail != null)
        {
            Console.WriteLine($"[BlendFileInfo] Disposing thumbnail resource");
            Thumbnail.Dispose();
            Thumbnail = null;
        }
        else
        {
            Console.WriteLine($"[BlendFileInfo] No thumbnail resource to dispose (null)");
        }
    }

    /// <summary>
    /// 格式化文件大小
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
        };
    }

    /// <summary>
    /// 获取文件扩展名
    /// </summary>
    public string Extension => !string.IsNullOrEmpty(FilePath) ? Path.GetExtension(FilePath) : string.Empty;

    /// <summary>
    /// 检查是否为有效的Blender文件
    /// </summary>
    public bool IsBlenderFile => Extension.Equals(".blend", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 获取相对路径（相对于指定目录）
    /// </summary>
    public string GetRelativePath(string basePath)
    {
        if (string.IsNullOrEmpty(FilePath) || string.IsNullOrEmpty(basePath))
            return FilePath;

        try
        {
            var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) ? basePath : basePath + Path.DirectorySeparatorChar);
            var fileUri = new Uri(FilePath);
            var relativeUri = baseUri.MakeRelativeUri(fileUri);
            return Uri.UnescapeDataString(relativeUri.ToString());
        }
        catch
        {
            return FilePath;
        }
    }
}
