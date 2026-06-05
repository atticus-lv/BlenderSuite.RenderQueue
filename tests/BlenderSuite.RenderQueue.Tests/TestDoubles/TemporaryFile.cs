using System;
using System.IO;

namespace BlenderSuite.RenderQueue.Tests;

internal sealed class TemporaryFile : IDisposable
{
    private TemporaryFile(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryFile Create(string extension)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, string.Empty);
        return new TemporaryFile(path);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // ignored
        }
    }
}
