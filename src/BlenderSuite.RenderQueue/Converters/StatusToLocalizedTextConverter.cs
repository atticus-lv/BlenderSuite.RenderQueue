using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Localizer;

namespace BlenderSuite.RenderQueue.Converters;

public class StatusToLocalizedTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RenderTaskStatus status) return string.Empty;
        var key = status.GetLocalizationKey();
        return Localizer.Localizer.Instance[key];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class QueueStateToLocalizedTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not QueueState state) return string.Empty;
        var key = state.GetLocalizationKey();
        return Localizer.Localizer.Instance[key];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}