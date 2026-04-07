using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BlenderSuite.RenderQueue.Models;

namespace QueueClient.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Brushes.Green : Brushes.Red;
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return !string.IsNullOrEmpty(str);
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StatusEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value != null && parameter != null)
        {
            return value.ToString() == parameter.ToString();
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? 1.0 : 0.5;
        }
        return 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 状态到颜色转换器
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RenderTaskStatus status)
        {
            return status switch
            {
                RenderTaskStatus.Pending => "#808080", // 灰色 - 等待中
                RenderTaskStatus.Running => "#00C000", // 绿色 - 运行中
                RenderTaskStatus.Paused => "#FFA500", // 橙色 - 已暂停
                RenderTaskStatus.Completed => "#008000", // 深绿色 - 已完成
                RenderTaskStatus.Failed => "#FF0000", // 红色 - 失败
                RenderTaskStatus.Cancelled => "#CCCCCC", // 浅灰色 - 已取消
                _ => "#CCCCCC" // 默认灰色
            };
        }

        return "#CCCCCC";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

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
        if (value is not string fileName) return value;
        return fileName.EndsWith(".blend", StringComparison.OrdinalIgnoreCase) ? fileName.Substring(0, fileName.Length - 6) : // 移除 ".blend" (6个字符)
            fileName;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 多值转换器，用于比较多个对象是否相等
/// </summary>
public class ObjectEqualMultiConverter : IMultiValueConverter
{
    public object Convert(IList<object?>? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2)
            return false;

        // 比较第一个值和第二个值是否相等
        return object.Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("ObjectEqualMultiConverter does not support ConvertBack");
    }
}

