using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Media.Imaging;

namespace BlenderRenderQueue.Helpers;

public static class BlendThumbnailExtractor
{
    private const int SIZEOF_INT = 4;
    private static readonly byte[] BLENDER_SIGNATURE = Encoding.ASCII.GetBytes("BLENDER");
    private static readonly byte[] TEST_SIGNATURE = Encoding.ASCII.GetBytes("TEST");

    public static unsafe Bitmap? ExtractThumbnail(string blendFilePath)
    {
        try
        {
            byte[] fileContent = File.ReadAllBytes(blendFilePath);
            if (fileContent.Length < 12)
            {
                Console.WriteLine("File too small to be a valid Blender file");
                return null;
            }

            // 检查Blender签名
            if (!StartsWith(fileContent, BLENDER_SIGNATURE))
            {
                var signature = Encoding.ASCII.GetString(fileContent, 0, 7);
                Console.WriteLine($"Invalid file signature: {signature} (expected: BLENDER)");
                return null;
            }

            // 解析文件头
            var is64Bit = fileContent[7] == '-';
            var isBigEndian = fileContent[8] == 'V';
            var sizeofBhead = is64Bit ? 24 : 20;

            Console.WriteLine($"Blender Version: {(char)fileContent[9]}.{(char)fileContent[10]}");
            Console.WriteLine($"File Format: {(is64Bit ? "64-bit" : "32-bit")}, {(isBigEndian ? "Big Endian" : "Little Endian")}");

            // 查找缩略图数据
            int position = 12;
            while (position + sizeofBhead <= fileContent.Length)
            {
                var code = new byte[4];
                Array.Copy(fileContent, position, code, 0, 4);
                var length = ReadInt32(fileContent, position + 4, isBigEndian);

                if (SequenceEqual(code, TEST_SIGNATURE))
                {
                    position += sizeofBhead;

                    // 读取图像尺寸
                    var width = ReadInt32(fileContent, position, isBigEndian);
                    var height = ReadInt32(fileContent, position + 4, isBigEndian);
                    position += 8;

                    Console.WriteLine($"Found image dimensions: {width}x{height}");

                    var imageDataLength = width * height * 4;
                    if (imageDataLength <= 0 || position + imageDataLength > fileContent.Length)
                    {
                        Console.WriteLine($"Invalid image dimensions or data length");
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

                    Console.WriteLine($"Successfully created thumbnail: {width}x{height}");
                    return bitmap;
                }
                else
                {
                    position += sizeofBhead + length;
                }
            }

            Console.WriteLine("No thumbnail data found");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting thumbnail: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
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