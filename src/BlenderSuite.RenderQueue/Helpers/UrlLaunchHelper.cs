using System;
using System.Diagnostics;

namespace BlenderSuite.RenderQueue.Helpers;

public static class UrlLaunchHelper
{
    public static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
