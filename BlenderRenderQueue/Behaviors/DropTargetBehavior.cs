using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace BlenderRenderQueue.Behaviors;

public static class DropTargetBehavior
{
    // 定义拖拽状态的颜色
    private static readonly IBrush DropTargetBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // 黄色 - 拖拽目标
    private static readonly IBrush DragTargetBrush = new SolidColorBrush(Color.FromRgb(0, 123, 255)); // 蓝色 - 拖拽源
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromRgb(0, 123, 255)); // 蓝色 - 选中状态
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromRgb(240, 248, 255)); // 浅蓝色 - 悬停状态
    
    public static void SetIsDropTarget(ListBoxItem element, bool value)
    {
        var currentState = element.Classes.Contains("isDropTarget");
        if (currentState == value) return; // 状态没有变化，直接返回
        
        if (value)
        {
            element.Classes.Add("isDropTarget");
            // 直接设置背景色
            SetBackgroundColor(element, DropTargetBrush);
        }
        else
        {
            element.Classes.Remove("isDropTarget");
            // 恢复默认背景色
            ResetBackgroundColor(element);
        }
        
        // 强制 UI 更新
        element.InvalidateVisual();
        element.InvalidateArrange();
        
        // 强制重新渲染
        element.InvalidateMeasure();
    }

    public static void SetIsDragTarget(ListBoxItem element, bool value)
    {
        var currentState = element.Classes.Contains("isDragTarget");
        if (currentState == value) return; // 状态没有变化，直接返回
        
        if (value)
        {
            element.Classes.Add("isDragTarget");
            // 直接设置背景色
            SetBackgroundColor(element, DragTargetBrush);
        }
        else
        {
            element.Classes.Remove("isDragTarget");
            // 恢复默认背景色
            ResetBackgroundColor(element);
        }
        
        // 强制 UI 更新
        element.InvalidateVisual();
        element.InvalidateArrange();
        
        // 强制重新渲染
        element.InvalidateMeasure();
    }
    
    private static void SetBackgroundColor(ListBoxItem element, IBrush brush)
    {
        // 查找 ListBoxItem 模板中的 Border 控件
        var border = FindBorderInTemplate(element);
        if (border != null)
        {
            border.Background = brush;
        }
    }
    
    private static void ResetBackgroundColor(ListBoxItem element)
    {
        // 查找 ListBoxItem 模板中的 Border 控件
        var border = FindBorderInTemplate(element);
        if (border != null)
        {
            // 根据当前状态恢复背景色
            if (element.IsSelected)
            {
                border.Background = SelectedBrush; // 选中状态颜色
            }
            else
            {
                border.Background = Brushes.Transparent; // 默认透明
            }
        }
    }
    
    private static Border? FindBorderInTemplate(ListBoxItem element)
    {
        // 遍历视觉树查找 Border 控件
        return FindBorderRecursive(element);
    }
    
    private static Border? FindBorderRecursive(Visual visual)
    {
        if (visual is Border border)
        {
            return border;
        }
        
        foreach (var child in visual.GetVisualChildren())
        {
            var result = FindBorderRecursive(child);
            if (result != null)
            {
                return result;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 强制清除所有拖拽状态，用于确保状态完全清理
    /// </summary>
    public static void ForceClearAllStates(ListBoxItem element)
    {
        // 强制移除所有拖拽相关的类
        element.Classes.Remove("isDropTarget");
        element.Classes.Remove("isDragTarget");
        
        // 强制重置背景色
        var border = FindBorderInTemplate(element);
        if (border != null)
        {
            if (element.IsSelected)
            {
                border.Background = SelectedBrush;
            }
            else
            {
                border.Background = Brushes.Transparent;
            }
        }
        
        // 强制 UI 更新
        element.InvalidateVisual();
        element.InvalidateArrange();
        element.InvalidateMeasure();
    }
}
