using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using SukiUI.Toasts;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Services.UI;

/// <summary>
///     A helper class to manage toasts via extension methods. Similar to DialogHelper but for toast notifications.
/// </summary>
public static class ToastHelper
{
    /// <summary>
    ///     Shows a toast notification for a registered context, most likely a ViewModel
    /// </summary>
    /// <param name="context">The context</param>
    /// <param name="title">The toast title</param>
    /// <param name="content">The toast content</param>
    /// <param name="type">The notification type</param>
    /// <param name="duration">How long to show the toast (default: 3 seconds)</param>
    /// <returns>True if toast was shown successfully, false otherwise</returns>
    /// <exception cref="ArgumentNullException">if context was null</exception>
    private static bool ShowToast(this object? context, string title, string content,
        NotificationType type = NotificationType.Information, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            // Get the TopLevel for the context
            var topLevel = ToplevelService.GetTopLevelForContext(context);
            if (topLevel == null) return false;

            // Get the ToastManager from the MainWindowViewModel
            var toastManager = GetToastManager(topLevel);
            if (toastManager == null) return false;

            // Create and show the toast
            toastManager.CreateToast()
                .WithTitle(title)
                .WithContent(content)
                .OfType(type)
                .Dismiss().After(duration ?? TimeSpan.FromSeconds(3))
                .Queue();

            return true;
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error showing toast: {ex.Message}", "ToastHelper");
            return false;
        }
    }


    public static bool ShowSuccessToast(this object? context, string title, string content,
        TimeSpan? duration = null)
    {
        return context.ShowToast(title, content, NotificationType.Success, duration);
    }


    public static bool ShowErrorToast(this object? context, string title, string content,
        TimeSpan? duration = null)
    {
        return context.ShowToast(title, content, NotificationType.Error, duration);
    }


    public static bool ShowWarningToast(this object? context, string title, string content,
        TimeSpan? duration = null)
    {
        return context.ShowToast(title, content, NotificationType.Warning, duration);
    }

    public static bool ShowInfoToast(this object? context, string title, string content,
        TimeSpan? duration = null)
    {
        return context.ShowToast(title, content, NotificationType.Information, duration);
    }

    /// <summary>
    ///     Shows a progress toast with a progress bar
    /// </summary>
    /// <param name="context">The context</param>
    /// <param name="title">The toast title</param>
    /// <param name="progressBar">The progress bar control</param>
    /// <param name="type">The notification type</param>
    /// <returns>The created toast instance for later updates</returns>
    public static ISukiToast? ShowProgressToast(this object? context, string title,
        ProgressBar progressBar, NotificationType type = NotificationType.Information)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            // Get the TopLevel for the context
            var topLevel = ToplevelService.GetTopLevelForContext(context);
            if (topLevel == null) return null;

            // Get the ToastManager from the MainWindowViewModel
            var toastManager = GetToastManager(topLevel);

            // Create and show the progress toast
            var toast = toastManager?.CreateToast()
                .WithTitle(title)
                .WithContent(progressBar)
                .OfType(type)
                .Queue();

            return toast;
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error showing progress toast: {ex.Message}", "ToastHelper");
            return null;
        }
    }

    /// <summary>
    ///     Updates a progress toast's progress bar value
    /// </summary>
    /// <param name="toast">The toast instance</param>
    /// <param name="progress">Progress value (0-100)</param>
    public static void UpdateProgressToast(this ISukiToast? toast, double progress)
    {
        if (toast == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (toast.Content is ProgressBar progressBar)
            {
                progressBar.Value = Math.Clamp(progress, 0, 100);
            }
        });
    }

    /// <summary>
    ///     Dismisses a toast
    /// </summary>
    /// <param name="context">The context</param>
    /// <param name="toast">The toast to dismiss</param>
    public static void DismissToast(this object? context, ISukiToast? toast)
    {
        if (context == null || toast == null) return;

        try
        {
            // Get the TopLevel for the context
            var topLevel = ToplevelService.GetTopLevelForContext(context);
            if (topLevel == null) return;

            // Get the ToastManager from the MainWindowViewModel
            var toastManager = GetToastManager(topLevel);
            toastManager?.Dismiss(toast);
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error dismissing toast: {ex.Message}", "ToastHelper");
        }
    }

    /// <summary>
    ///     Gets the ToastManager from the MainWindowViewModel
    /// </summary>
    /// <param name="topLevel">The top level window</param>
    /// <returns>The ToastManager or null if not found</returns>
    private static ISukiToastManager? GetToastManager(TopLevel topLevel)
    {
        try
        {
            // Get the DataContext of the top level (should be MainWindowViewModel)
            if (topLevel.DataContext is MainWindowViewModel mainWindowViewModel)
            {
                return mainWindowViewModel.ToastManager;
            }

            return null;
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error getting ToastManager: {ex.Message}", "ToastHelper");
            return null;
        }
    }
}