using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application;
using BlenderSuite.RenderQueue.Services.Application.Logging;

namespace BlenderSuite.RenderQueue.Services.Business.Persistence;

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
    private readonly string _defaultDataFilePath;
    private readonly IRenderLogService _logService;

    public DataPersistenceService(IRenderLogService logService)
    {
        _logService = logService;
        _defaultDataFilePath = Path.Combine(ApplicationPaths.GetAppDataDirectory(), "data.json");
        EnsureDataFileDirectory(_defaultDataFilePath);
    }

    public async Task<bool> SaveDataAsync(AppData data, string? filePath = null)
    {
        string? tempFilePath = null;
        var targetFilePath = ResolveDataFilePath(filePath);
        var operation = _logService.BeginOperation(
            RenderLogScope.Recovery,
            "SaveQueueData",
            nameof(DataPersistenceService),
            "开始保存队列数据。",
            metadata: new Dictionary<string, string>
            {
                ["path"] = targetFilePath,
                ["task_count"] = data.RenderQueue.Count.ToString()
            });
        try
        {
            EnsureDataFileDirectory(targetFilePath);
            var json = JsonSerializer.Serialize(data, AppDataJsonContext.Default.AppData);
            tempFilePath = $"{targetFilePath}.{Guid.NewGuid():N}.tmp";
            operation.Detail($"写入临时数据文件: {tempFilePath}");

            await File.WriteAllTextAsync(tempFilePath, json);
            File.Move(tempFilePath, targetFilePath, overwrite: true);

            operation.Complete(
                $"队列数据保存完成，任务数: {data.RenderQueue.Count}",
                audience: RenderLogMetadata.AudienceDiagnostic);
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

            operation.Fail($"队列数据保存失败: {ex.Message}");
            return false;
        }
    }

    public async Task<AppData> LoadDataAsync(string? filePath = null)
    {
        var targetFilePath = ResolveDataFilePath(filePath);
        var operation = _logService.BeginOperation(
            RenderLogScope.Recovery,
            "LoadQueueDataFile",
            nameof(DataPersistenceService),
            "开始读取队列数据文件。",
            metadata: new Dictionary<string, string>
            {
                ["path"] = targetFilePath
            });
        try
        {
            if (!DataFileExists(targetFilePath))
            {
                operation.Complete(
                    "未找到队列数据文件，使用默认空队列。",
                    RenderLogLevel.Warning,
                    audience: RenderLogMetadata.AudienceDiagnostic);
                return new AppData();
            }

            operation.Detail($"读取队列数据文件: {targetFilePath}");

            // 异步读取文件
            var json = await File.ReadAllTextAsync(targetFilePath);

            // 反序列化 JSON
            var data = JsonSerializer.Deserialize(json, AppDataJsonContext.Default.AppData);

            if (data == null)
            {
                operation.Fail("队列数据反序列化失败，使用默认空队列。");
                return new AppData();
            }

            if (!IsSupportedQueueData(data))
            {
                operation.Fail("队列数据身份或格式版本不匹配，使用默认空队列。");
                return new AppData();
            }

            operation.Complete(
                $"队列数据文件读取完成，任务数: {data.RenderQueue.Count}",
                audience: RenderLogMetadata.AudienceDiagnostic);
            return data;
        }
        catch (Exception ex)
        {
            operation.Fail($"队列数据读取失败: {ex.Message}");
            return new AppData();
        }
    }

    public bool DataFileExists(string? filePath = null)
    {
        return File.Exists(ResolveDataFilePath(filePath));
    }

    public bool DeleteDataFile(string? filePath = null)
    {
        var targetFilePath = ResolveDataFilePath(filePath);
        try
        {
            if (DataFileExists(targetFilePath))
            {
                File.Delete(targetFilePath);
                _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"✅ Data file deleted: {targetFilePath}", source: "DataPersistenceService");
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

    private static bool IsSupportedQueueData(AppData data)
    {
        return string.Equals(data.ApplicationId, ApplicationIdentity.ProductId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(data.Schema, ApplicationIdentity.QueueDataSchema, StringComparison.Ordinal) &&
               data.SchemaVersion == ApplicationIdentity.QueueDataSchemaVersion;
    }

    private string ResolveDataFilePath(string? filePath)
    {
        return string.IsNullOrWhiteSpace(filePath) ? _defaultDataFilePath : filePath;
    }

    private static void EnsureDataFileDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
