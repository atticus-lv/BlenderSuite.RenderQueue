using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Business.Persistence;
using Xunit;

namespace BlenderSuite.RenderQueue.Tests.Services.Business.Persistence;

[Collection("AppDataPersistenceEnvironment")]
public sealed class DataPersistenceServiceTests
{
    [Fact]
    public async Task SaveDataAsync_WritesStableQueueDocumentMetadata()
    {
        using var appDataDirectory = TemporaryAppDataDirectory.Create();
        var sut = new DataPersistenceService(TestLogServiceFactory.Create());

        var saved = await sut.SaveDataAsync(new AppData());

        Assert.True(saved);

        var dataPath = Path.Combine(appDataDirectory.Path, "data.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(dataPath, TestContext.Current.CancellationToken));
        var root = document.RootElement;

        Assert.Equal(ApplicationIdentity.ProductId, root.GetProperty("ApplicationId").GetString());
        Assert.Equal(ApplicationIdentity.ProductName, root.GetProperty("ApplicationName").GetString());
        Assert.Equal(ApplicationIdentity.QueueDataSchema, root.GetProperty("Schema").GetString());
        Assert.Equal(ApplicationIdentity.QueueDataSchemaVersion, root.GetProperty("SchemaVersion").GetInt32());
        Assert.True(root.TryGetProperty("AppVersion", out _));
        Assert.True(root.TryGetProperty("BatchId", out _));
        Assert.Equal(string.Empty, root.GetProperty("BatchName").GetString());
        Assert.True(root.TryGetProperty("CreatedAt", out _));
        Assert.True(root.TryGetProperty("UpdatedAt", out _));
        Assert.False(root.TryGetProperty("Software", out _));
    }

    [Fact]
    public async Task SaveDataAsync_WhenFilePathProvided_WritesBatchFile()
    {
        using var appDataDirectory = TemporaryAppDataDirectory.Create();
        var batchPath = Path.Combine(appDataDirectory.Path, "batches", "shot-010.json");
        var sut = new DataPersistenceService(TestLogServiceFactory.Create());

        var saved = await sut.SaveDataAsync(new AppData(), batchPath);

        Assert.True(saved);
        Assert.True(File.Exists(batchPath));
    }

    [Fact]
    public async Task LoadDataAsync_WhenApplicationIdDoesNotMatch_ReturnsEmptyQueue()
    {
        using var appDataDirectory = TemporaryAppDataDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(appDataDirectory.Path, "data.json"), """
            {
              "ApplicationId": "00000000-0000-0000-0000-000000000000",
              "ApplicationName": "Other.App",
              "Schema": "render-queue",
              "SchemaVersion": 1,
              "AppVersion": "1.0.0.0",
              "BatchId": "3f95c6f6-b54f-4130-9d0d-4fb60f2f239f",
              "BatchName": "",
              "CreatedAt": "2026-06-05T00:00:00Z",
              "UpdatedAt": "2026-06-05T00:00:00Z",
              "RenderQueue": [
                {
                  "RenderTask": {
                    "Id": "bfa6b8b9-e0fe-4b03-9153-cf471f011a8a",
                    "Filename": "bad.blend",
                    "Filepath": "/tmp/bad.blend",
                    "StartFrame": 1,
                    "EndFrame": 1,
                    "LastRenderedFrame": 0,
                    "Enable": true
                  }
                }
              ]
            }
            """, TestContext.Current.CancellationToken);
        var sut = new DataPersistenceService(TestLogServiceFactory.Create());

        var loaded = await sut.LoadDataAsync();

        Assert.Empty(loaded.RenderQueue);
    }

    private sealed class TemporaryAppDataDirectory : IDisposable
    {
        private const string AppDataOverrideEnv = "BSRQ_APP_DATA_DIR";
        private readonly string? _previousValue;

        private TemporaryAppDataDirectory(string path, string? previousValue)
        {
            Path = path;
            _previousValue = previousValue;
        }

        public string Path { get; }

        public static TemporaryAppDataDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            var previousValue = Environment.GetEnvironmentVariable(AppDataOverrideEnv);
            Environment.SetEnvironmentVariable(AppDataOverrideEnv, path);
            return new TemporaryAppDataDirectory(path, previousValue);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataOverrideEnv, _previousValue);
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }
}

[CollectionDefinition("AppDataPersistenceEnvironment", DisableParallelization = true)]
public sealed class AppDataPersistenceEnvironmentCollection;
