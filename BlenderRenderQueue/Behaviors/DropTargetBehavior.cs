using System;
using Avalonia.Controls;

namespace BlenderRenderQueue.Behaviors;

public static class DropTargetBehavior
{
    public static void SetIsDropTarget(ListBoxItem element, bool value)
    {
        Console.WriteLine($"DropTargetBehavior.SetIsDropTarget: Called with value: {value}");
        if (value)
        {
            element.Classes.Add("isDropTarget");
            Console.WriteLine($"DropTargetBehavior.SetIsDropTarget: Adding isDropTarget class");
        }
        else
        {
            element.Classes.Remove("isDropTarget");
            Console.WriteLine($"DropTargetBehavior.SetIsDropTarget: Removing isDropTarget class");
        }
        Console.WriteLine($"DropTargetBehavior.SetIsDropTarget: Current classes: {string.Join(", ", element.Classes)}");
    }

    public static void SetIsDragTarget(ListBoxItem element, bool value)
    {
        Console.WriteLine($"DropTargetBehavior.SetIsDragTarget: Called with value: {value}");
        if (value)
        {
            element.Classes.Add("isDragTarget");
            Console.WriteLine($"DropTargetBehavior.SetIsDragTarget: Adding isDragTarget class");
        }
        else
        {
            element.Classes.Remove("isDragTarget");
            Console.WriteLine($"DropTargetBehavior.SetIsDragTarget: Removing isDragTarget class");
        }
        Console.WriteLine($"DropTargetBehavior.SetIsDragTarget: Current classes: {string.Join(", ", element.Classes)}");
    }
}
