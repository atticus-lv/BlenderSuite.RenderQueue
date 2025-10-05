using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Converters;

public class StatusToIconVisibilityConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 4 || values[0] is not bool isValid || values[1] is not bool enable || 
            values[2] is not bool isLoading || values[3] is not RenderTaskStatus status)
        {
            return false;
        }

        // 参数用于区分不同的图标类型
        string? iconType = parameter?.ToString();

        switch (iconType)
        {
            case "CompletedButDisabled":
                // 显示已完成但禁用的图标
                return isValid && !enable && !isLoading && status == RenderTaskStatus.Completed;
            
            case "NotRender":
                // 显示未渲染的图标（待处理状态且禁用）
                return isValid && !enable && !isLoading && status == RenderTaskStatus.Pending;
            
            default:
                return false;
        }
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
