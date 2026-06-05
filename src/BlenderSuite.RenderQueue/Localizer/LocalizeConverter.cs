using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BlenderSuite.RenderQueue.Localizer;

public class LocalizeConverter : IValueConverter, IMultiValueConverter
{
    public static readonly LocalizeConverter Instance = new LocalizeConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            // 检查是否是特殊格式的字符串（如 "Queue_RemainingTimeFormat:01:23:45"）
            if (key.Contains(':'))
            {
                var parts = key.Split(':', 2);
                if (parts.Length == 2)
                {
                    var translationKey = parts[0];
                    var formatValue = parts[1];
                    var translatedFormat = Localizer.Instance[translationKey];
                    
                    try
                    {
                        return string.Format(translatedFormat, formatValue);
                    }
                    catch (FormatException)
                    {
                        // 如果格式化失败，返回翻译文本
                        return translatedFormat;
                    }
                }
            }
            
            return Localizer.Instance[key];
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values != null && values.Count > 0)
        {
            // 如果第一个值是字符串，将其作为翻译键
            if (values[0] is string key)
            {
                var translatedText = Localizer.Instance[key];
                
                // 如果有多个值，使用 String.Format 来格式化
                if (values.Count > 1)
                {
                    try
                    {
                        var formatArgs = new object[values.Count - 1];
                        for (int i = 1; i < values.Count; i++)
                        {
                            formatArgs[i - 1] = values[i] ?? string.Empty;
                        }
                        return string.Format(translatedText, formatArgs);
                    }
                    catch (FormatException)
                    {
                        // 如果格式化失败，返回翻译文本
                        return translatedText;
                    }
                }
                
                return translatedText;
            }
            
            // 如果第一个值不是字符串，直接返回第一个值
            return values[0] ?? string.Empty;
        }

        return string.Empty;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return new object[] { value ?? string.Empty };
    }
}