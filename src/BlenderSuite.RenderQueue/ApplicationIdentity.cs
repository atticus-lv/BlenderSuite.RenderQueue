using System.Reflection;

namespace BlenderSuite.RenderQueue;

internal static class ApplicationIdentity
{
    public const string ProductId = "a8239aab-c146-434c-85c1-d6d56bc9b77c";
    public const string ProductName = "BlenderSuite.RenderQueue";
    public const string ProductDisplayName = "Blender Suite: Render Queue";
    public const string MacBundleIdentifier = "com.atticus.blenderrenderqueue";
    public const string AppDataDirectoryName = ProductName;

    public const string QueueDataSchema = "render-queue";
    public const int QueueDataSchemaVersion = 1;

    public static string GetAppVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    }
}
