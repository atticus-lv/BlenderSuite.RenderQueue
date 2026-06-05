using System.IO;
using BlenderSuite.RenderQueue.Services.Application;

namespace BlenderSuite.RenderQueue.Services.Business.Submission;

internal static class SubmissionPaths
{
    private const string EndpointFileName = "submission_endpoint.json";

    public static string GetEndpointFilePath()
    {
        var appDataDirectory = ApplicationPaths.GetAppDataDirectory();
        Directory.CreateDirectory(appDataDirectory);
        return Path.Combine(appDataDirectory, EndpointFileName);
    }
}
