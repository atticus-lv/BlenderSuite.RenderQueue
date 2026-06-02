using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Services.Business.Persistence;

[JsonSerializable(typeof(AppData))]
[JsonSerializable(typeof(RenderTaskData))]
[JsonSerializable(typeof(RenderTaskInfo))]
[JsonSerializable(typeof(OverrideData))]
[JsonSerializable(typeof(OverrideFrameRangeData))]
[JsonSerializable(typeof(OverrideSceneData))]
internal partial class AppDataJsonContext : JsonSerializerContext
{
}

/// <summary>
/// 数据持久化服务实现
/// </summary>
public class DataPersistenceService : IDataPersistenceService
{
    private readonly string _dataFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IRenderLogService _logService;

    public DataPersistenceService(IRenderLogService logService)
    {
        _logService = logService;
        // 数据文件路径：运行目录下的 data.json
        // _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.json");
        _dataFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlenderRenderQueue",
            "data.json"
        );
        var directory = Path.GetDirectoryName(_dataFilePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        // JSON 序列化选项 - 配置为支持 AOT 编译
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true, // 格式化输出，便于阅读
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = AppDataJsonContext.Default
        };
    }

    public async Task<bool> SaveDataAsync(AppData data)
    {
        string? tempFilePath = null;
        try
        {
            _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"Saving data to: {_dataFilePath}", source: "DataPersistenceService");

            var json = JsonSerializer.Serialize(data, AppDataJsonContext.Default.AppData);
            tempFilePath = $"{_dataFilePath}.{Guid.NewGuid():N}.tmp";

            await File.WriteAllTextAsync(tempFilePath, json);
            File.Move(tempFilePath, _dataFilePath, overwrite: true);

            _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"✅ Data saved successfully", source: "DataPersistenceService");
            return true;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(tempFilePath) && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                    // ignored
                }
            }

            _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"❌ Error saving data: {ex.Message}", source: "DataPersistenceService");
            return false;
        }
    }

    public async Task<AppData> LoadDataAsync()
    {
        try
        {
            if (!DataFileExists())
            {
                _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"Data file does not exist, returning default data", source: "DataPersistenceService");
                return new AppData();
            }

            _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"Loading data from: {_dataFilePath}", source: "DataPersistenceService");

            // 异步读取文件
            var json = await File.ReadAllTextAsync(_dataFilePath);

            // 反序列化 JSON
            var data = JsonSerializer.Deserialize(json, AppDataJsonContext.Default.AppData);

            if (data == null)
            {
                _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"❌ Failed to deserialize data, returning default", source: "DataPersistenceService");
                return new AppData();
            }

            _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"✅ Data loaded successfully - Tasks: {data.RenderQueue.Count}", source: "DataPersistenceService");
            return data;
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"❌ Error loading data: {ex.Message}", source: "DataPersistenceService");
            return new AppData();
        }
    }

    public bool DataFileExists()
    {
        return File.Exists(_dataFilePath);
    }

    public bool DeleteDataFile()
    {
        try
        {
            if (DataFileExists())
            {
                File.Delete(_dataFilePath);
                _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"✅ Data file deleted: {_dataFilePath}", source: "DataPersistenceService");
                return true;
            }

            return true; // 文件不存在也算删除成功
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"❌ Error deleting data file: {ex.Message}", source: "DataPersistenceService");
            return false;
        }
    }
}
