using System;
using Avalonia.Data.Converters;

namespace BlenderRenderQueue.Converters;

public class WidthToScaleConverter : IValueConverter
{
    public static readonly WidthToScaleConverter Instance = new();
    
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double width && parameter is string scaleStr &&
            double.TryParse(scaleStr, out double scale))
        {
            var calculatedWidth = width * scale;
            
            // 应用最小和最大宽度限制
            var minWidth = 300.0;
            var maxWidth = 450.0;
            
            return Math.Max(minWidth, Math.Min(maxWidth, calculatedWidth));
        }

        // 如果转换失败，返回默认宽度
        return 350.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}