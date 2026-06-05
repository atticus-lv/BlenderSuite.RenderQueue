using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Input;

namespace BlenderSuite.RenderQueue.Behaviors;

internal static class DropFileTransferHelper
{
    public static bool ContainsFileDropData(DragEventArgs e)
    {
        return e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.Contains(DataFormat.Text);
    }

    public static IReadOnlyList<string> ExtractFilePaths(DragEventArgs e)
    {
        var filePaths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => item.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filePaths.Count > 0)
        {
            return filePaths;
        }

        var text = e.DataTransfer.TryGetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
