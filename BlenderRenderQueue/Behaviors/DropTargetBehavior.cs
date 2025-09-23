using Avalonia.Controls;

namespace BlenderRenderQueue.Behaviors;

public static class DropTargetBehavior
{
    public static void SetIsDropTarget(ListBoxItem element, bool value)
    {
        if (value)
        {
            element.Classes.Add("isDropTarget");
        }
        else
        {
            element.Classes.Remove("isDropTarget");
        }
    }

    public static void SetIsDragTarget(ListBoxItem element, bool value)
    {
        if (value)
        {
            element.Classes.Add("isDragTarget");
        }
        else
        {
            element.Classes.Remove("isDragTarget");
        }
    }
}
