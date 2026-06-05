using System;
using System.IO;

namespace BlenderSuite.RenderQueue.Services.Application;

internal static class ApplicationPaths
{
    private const string AppDataOverrideEnv = "BRQ_APP_DATA_DIR";

    public static string GetAppDataDirectory()
    {
        var explicitPath = Environment.GetEnvironmentVariable(AppDataOverrideEnv);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlenderSuite.RenderQueue");
    }
}
