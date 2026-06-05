using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BlenderSuite.RenderQueue.Models;

namespace BlenderSuite.RenderQueue.Converters;

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