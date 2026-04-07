using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BlenderRenderQueue.Converters;

/// <summary>
/// 文件名转换器，用于去除文件扩展名
/// </summary>
public class FileNameConverter : IValueConverter
{
    /// <summary>
    /// 静态实例，用于在 XAML 中通过 x:Static 引用
    /// </summary>
    public static readonly FileNameConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string fileName)
        {
            // 去除 .blend 后缀
            if (fileName.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - 6); // 移除 ".blend" (6个字符)
            }
            
            // 如果没有 .blend 后缀，返回原文件名
            return fileName;
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
} 