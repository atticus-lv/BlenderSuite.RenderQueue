using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BlenderRenderQueue.Converters;

/// <summary>
/// 多值转换器，用于比较多个对象是否相等
/// </summary>
public class ObjectEqualMultiConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
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
