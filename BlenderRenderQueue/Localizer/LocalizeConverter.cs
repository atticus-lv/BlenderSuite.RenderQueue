using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BlenderRenderQueue.Localizer;

public class LocalizeConverter : IValueConverter
{
    public static readonly LocalizeConverter Instance = new LocalizeConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            return Localizer.Instance[key];
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}