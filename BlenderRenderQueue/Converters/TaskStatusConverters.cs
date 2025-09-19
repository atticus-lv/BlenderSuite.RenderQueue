using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Converters;

/// <summary>
/// 检查任务是否可以被操作（没有任务在渲染时）
/// </summary>
public class IsTaskOperableConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RenderTaskViewModel task)
        {
            // 如果任务正在运行，则不可操作
            if (task.Status == RenderTaskStatus.Running)
                return false;

            return true;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}