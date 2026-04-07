using System;
using System.IO;

namespace BlenderRenderQueue.Services.Business.Submission;

internal static class SubmissionPaths
{
    private const string AppDataOverrideEnv = "BRQ_APP_DATA_DIR";
    private const string EndpointFileName = "submission_endpoint.json";

    public static string GetAppDataDirectory()
    {
        var explicitPath = Environment.GetEnvironmentVariable(AppDataOverrideEnv);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlenderRenderQueue");
    }

    public static string GetEndpointFilePath()
    {
        var appDataDirectory = GetAppDataDirectory();
        Directory.CreateDirectory(appDataDirectory);
        return Path.Combine(appDataDirectory, EndpointFileName);
    }
}
