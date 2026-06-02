using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Media.Imaging;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Helpers;

public enum ThumbnailExtractionStatus
{
    Success = 0,
    FileError = 1,
    CompressionError = 2,
    DecompressionError = 3,
    InvalidFile = 4,
    EarlyVersion = 5,
    InvalidThumbnail = 6,
    Error = 9
}

public static class BlendThumbnailExtractor
{
    private const int SIZEOF_INT = 4;
    private static readonly byte[] BLENDER_SIGNATURE = Encoding.ASCII.GetBytes("BLENDER");
    private static readonly byte[] TEST_SIGNATURE = Encoding.ASCII.GetBytes("TEST");
    private static readonly byte[] REND_SIGNATURE = Encoding.ASCII.GetBytes("REND");

    public static unsafe Bitmap? ExtractThumbnail(string blendFilePath)
    {
        var result = ExtractThumbnailWithStatus(blendFilePath, out var status);
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Extraction status: {status}", "BlendThumbnailExtractor");
        return result;
    }

    public static unsafe Bitmap? ExtractThumbnailWithStatus(string blendFilePath, out ThumbnailExtractionStatus status)
    {
        status = ThumbnailExtractionStatus.Error;

        try
        {
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Starting extraction from: {blendFilePath}", "BlendThumbnailExtractor");

            byte[] fileContent = File.ReadAllBytes(blendFilePath);
            if (fileContent.Length < 12)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, "File too small to be a valid Blender file", "BlendThumbnailExtractor");
                status = ThumbnailExtractionStatus.InvalidFile;
                return null;
            }

            // 检查Blender签名
            if (!StartsWith(fileContent, BLENDER_SIGNATURE))
            {
                var signature = Encoding.ASCII.GetString(fileContent, 0, 7);
                ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Invalid file signature: {signature} (expected: BLENDER)", "BlendThumbnailExtractor");
                status = ThumbnailExtractionStatus.InvalidFile;
                return null;
            }

            // 解析文件头
            var is64Bit = fileContent[7] == '-';
            var isBigEndian = fileContent[8] == 'V';
            var sizeofBhead = is64Bit ? 24 : 20;

            // 检查字节序 - 只支持小端序
            if (isBigEndian)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, "Big-endian files are not supported", "BlendThumbnailExtractor");
                status = ThumbnailExtractionStatus.InvalidFile;
                return null;
            }

            // 检查版本 - 需要2.50或更高版本才有缩略图
            var majorVersion = (char)fileContent[9];
            var minorVersion = (char)fileContent[10];
            var versionString = $"{majorVersion}.{minorVersion}";

            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Blender Version: {versionString}", "BlendThumbnailExtractor");
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, $"File Format: {(is64Bit ? "64-bit" : "32-bit")}, Little Endian", "BlendThumbnailExtractor");

            // 检查版本是否足够新（2.50+）
            if (majorVersion < '2' || (majorVersion == '2' && minorVersion < '5'))
            {
                ApplicationLogWriter.Write(RenderLogLevel.Warning, RenderLogScope.Task, $"Version {versionString} is too old, thumbnails require 2.50+", "BlendThumbnailExtractor");
                status = ThumbnailExtractionStatus.EarlyVersion;
                return null;
            }

            // 查找缩略图数据
            int position = 12;
            bool foundTestOrRend = false;

            while (position + sizeofBhead <= fileContent.Length)
            {
                var code = new byte[4];
                Array.Copy(fileContent, position, code, 0, 4);
                var length = ReadInt32(fileContent, position + 4, isBigEndian);

                if (length < 0)
                {
                    ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.Task, "Invalid block length", "BlendThumbnailExtractor");
                    status = ThumbnailExtractionStatus.InvalidThumbnail;
                    return null;
                }

                if (SequenceEqual(code, TEST_SIGNATURE))
                {
                    foundTestOrRend = true;
                    position += sizeofBhead;

                    // 读取图像尺寸
                    var width = ReadInt32(fileContent, position, isBigEndian);
                    var height = ReadInt32(fileContent, position + 4, isBigEndian);
                    position += 8;

                    ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Found TEST block with image dimensions: {width}x{height}", "BlendThumbnailExtractor");

                    // 验证尺寸
                    if (width <= 0 || height <= 0)
                    {
                        ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.Task, "Invalid image dimensions", "BlendThumbnailExtractor");
                        status = ThumbnailExtractionStatus.InvalidThumbnail;
                        return null;
                    }

                    var imageDataLength = width * height * 4;
                    if (position + imageDataLength > fileContent.Length)
                    {
                        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, "Image data extends beyond file", "BlendThumbnailExtractor");
                        status = ThumbnailExtractionStatus.InvalidThumbnail;
                        return null;
                    }

                    // 创建位图
                    var bitmap = new WriteableBitmap(
                        new Avalonia.PixelSize(width, height),
                        new Avalonia.Vector(96, 96),
                        Avalonia.Platform.PixelFormat.Bgra8888,
                        Avalonia.Platform.AlphaFormat.Premul);

                    using (var lockedBitmap = bitmap.Lock())
                    {
                        // 复制并转换像素数据
                        byte* destPtr = (byte*)lockedBitmap.Address;
                        for (int i = 0; i < width * height; i++)
                        {
                            int srcOffset = position + (i * 4);
                            int destOffset = i * 4;

                            // Blender使用RGBA格式，需要转换为BGRA
                            destPtr[destOffset + 0] = fileContent[srcOffset + 2]; // B
                            destPtr[destOffset + 1] = fileContent[srcOffset + 1]; // G
                            destPtr[destOffset + 2] = fileContent[srcOffset + 0]; // R
                            destPtr[destOffset + 3] = fileContent[srcOffset + 3]; // A
                        }
                    }

                    // 垂直翻转图像
                    FlipImageVertical(bitmap);

                    ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Successfully created thumbnail: {width}x{height}", "BlendThumbnailExtractor");
                    status = ThumbnailExtractionStatus.Success;
                    return bitmap;
                }
                else if (SequenceEqual(code, REND_SIGNATURE))
                {
                    foundTestOrRend = true;
                    ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.Task, "Found REND block, skipping", "BlendThumbnailExtractor");
                    position += sizeofBhead + length;
                }
                else
                {
                    // 如果已经找到了TEST或REND块，但当前块不是，则继续
                    if (foundTestOrRend)
                    {
                        position += sizeofBhead + length;
                    }
                    else
                    {
                        // 如果还没找到TEST或REND块，且当前块也不是，则提前退出
                        ApplicationLogWriter.Write(RenderLogLevel.Warning, RenderLogScope.Task, "No TEST or REND blocks found, early exit", "BlendThumbnailExtractor");
                        status = ThumbnailExtractionStatus.InvalidThumbnail;
                        return null;
                    }
                }
            }

            ApplicationLogWriter.Write(RenderLogLevel.Warning, RenderLogScope.Task, "No thumbnail data found", "BlendThumbnailExtractor");
            status = ThumbnailExtractionStatus.InvalidThumbnail;
            return null;
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Error extracting thumbnail: {ex.Message}", "BlendThumbnailExtractor");
            ApplicationLogWriter.Write(RenderLogLevel.Debug, RenderLogScope.Task, ex.StackTrace ?? string.Empty, "BlendThumbnailExtractor");
            status = ThumbnailExtractionStatus.Error;
            return null;
        }
    }

    private static void FlipImageVertical(WriteableBitmap bitmap)
    {
        using var locked = bitmap.Lock();
        unsafe
        {
            byte* ptr = (byte*)locked.Address;
            int stride = locked.RowBytes;
            int height = bitmap.PixelSize.Height;
            byte[] tempRow = new byte[stride];

            for (int y = 0; y < height / 2; y++)
            {
                int topOffset = y * stride;
                int bottomOffset = (height - 1 - y) * stride;

                // 使用 fixed 来固定数组位置
                fixed (byte* pTemp = tempRow)
                {
                    Buffer.MemoryCopy(ptr + topOffset, pTemp, stride, stride);
                    Buffer.MemoryCopy(ptr + bottomOffset, ptr + topOffset, stride, stride);
                    Buffer.MemoryCopy(pTemp, ptr + bottomOffset, stride, stride);
                }
            }
        }
    }

    private static int ReadInt32(byte[] buffer, int offset, bool isBigEndian)
    {
        if (isBigEndian)
        {
            return (buffer[offset] << 24) | (buffer[offset + 1] << 16) |
                   (buffer[offset + 2] << 8) | buffer[offset + 3];
        }
        return (buffer[offset + 3] << 24) | (buffer[offset + 2] << 16) |
               (buffer[offset + 1] << 8) | buffer[offset];
    }

    private static bool StartsWith(byte[] array, byte[] pattern)
    {
        if (array.Length < pattern.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (array[i] != pattern[i]) return false;
        }
        return true;
    }

    private static bool SequenceEqual(byte[] array1, byte[] array2)
    {
        if (array1.Length != array2.Length) return false;
        return !array1.Where((t, i) => t != array2[i]).Any();
    }
}
