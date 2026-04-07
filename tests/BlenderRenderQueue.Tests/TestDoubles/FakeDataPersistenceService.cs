using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Persistence;

namespace BlenderRenderQueue.Tests;

internal sealed class FakeDataPersistenceService : IDataPersistenceService
{
    public AppData SavedData { get; private set; } = new();
    public AppData LoadedData { get; set; } = new();

    public Task<bool> SaveDataAsync(AppData data)
    {
        SavedData = data;
        return Task.FromResult(true);
    }

    public Task<AppData> LoadDataAsync()
    {
        return Task.FromResult(LoadedData);
    }

    public bool DataFileExists() => false;

    public bool DeleteDataFile() => true;
}
