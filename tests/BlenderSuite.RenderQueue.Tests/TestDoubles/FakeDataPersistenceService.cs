using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Business.Persistence;

namespace BlenderSuite.RenderQueue.Tests;

internal sealed class FakeDataPersistenceService : IDataPersistenceService
{
    public AppData SavedData { get; private set; } = new();
    public AppData LoadedData { get; set; } = new();

    public Task<bool> SaveDataAsync(AppData data, string? filePath = null)
    {
        SavedData = data;
        return Task.FromResult(true);
    }

    public Task<AppData> LoadDataAsync(string? filePath = null)
    {
        return Task.FromResult(LoadedData);
    }

    public bool DataFileExists(string? filePath = null) => false;

    public bool DeleteDataFile(string? filePath = null) => true;
}
