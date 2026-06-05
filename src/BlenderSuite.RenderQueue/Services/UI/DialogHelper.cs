using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace BlenderSuite.RenderQueue.Services.UI;

/// <summary>
///     A helper class to manage dialogs via extension methods. Add more on your own
/// </summary>
public static class DialogHelper
{
    /// <summary>
    ///     Shows an open file dialog for a registered context, most likely a ViewModel
    /// </summary>
    /// <param name="context">The context</param>
    /// <param name="title">The dialog title or a default is null</param>
    /// <param name="selectMany">Is selecting many files allowed?</param>
    /// <param name="fileTypes">The file types to filter the dialog</param>
    /// <returns>An array of file names</returns>
    /// <exception cref="ArgumentNullException">if context was null</exception>
    public static async Task<IEnumerable<string>?> OpenFileDialogAsync(this object? context, string? title = null,
        bool selectMany = true, bool selectFolder = false, IEnumerable<FilePickerFileType>? fileTypes = null)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        // lookup the TopLevel for the context
        var topLevel = ToplevelService.GetTopLevelForContext(context);

        if (topLevel == null) return null;

        if (!selectFolder)
        {
            var storageFiles = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    AllowMultiple = selectMany,
                    Title = title ?? "Select any file(s)",
                    FileTypeFilter = fileTypes?.ToArray()
                });
            return storageFiles.Count == 0
                ? null
                : storageFiles.Select(s => s.Path.LocalPath);
        }
        else
        {
            var storageFiles = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    AllowMultiple = selectMany,
                    Title = title ?? "Select a folder"
                });
            if (storageFiles.Count == 0) return null;
            return storageFiles.Select(s => s.Path.LocalPath);
        }
    }

    public delegate void SetSelectedPath(string path);

    public static async Task ChangeDirectory(this object? context, SetSelectedPath setDirectory,
        string title = "Select")
    {
        var selectedFiles = await context.OpenFileDialogAsync(title, false, true);
        if (selectedFiles is null) return;
        setDirectory(selectedFiles.ElementAt(0));
    }

    public static async Task<string> SelectFile(this object? context, string title = "Select",
        IEnumerable<FilePickerFileType>? fileTypes = null)
    {
        var selectedFiles = await context.OpenFileDialogAsync(
            title,
            false,
            false,
            fileTypes);
        return selectedFiles is null ? string.Empty : selectedFiles.ElementAt(0);
    }

    public static async Task<List<string>> SelectFiles(this object? context, string title = "Select",
        IEnumerable<FilePickerFileType>? fileTypes = null)
    {
        var selectedFiles = await context.OpenFileDialogAsync(
            title,
            true,
            false,
            fileTypes);
        return selectedFiles is null ? [] : selectedFiles.ToList();
    }
}
