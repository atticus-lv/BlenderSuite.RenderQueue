using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services;

/// <summary>
/// 数据持久化服务实现
/// </summary>
public class DataPersistenceService : IDataPersistenceService
{
    private readonly string _dataFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public DataPersistenceService()
    {
        // 数据文件路径：运行目录下的 data.json
        _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.json");
        
        // JSON 序列化选项 - 配置为支持 AOT 编译
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true, // 格式化输出，便于阅读
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            // 使用 DefaultJsonTypeInfoResolver 来支持反射序列化，同时保持 AOT 兼容性
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
    }

    public async Task<bool> SaveDataAsync(AppData data)
    {
        try
        {
            Console.WriteLine($"[DataPersistenceService] Saving data to: {_dataFilePath}");
            
            // 序列化为 JSON
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            
            // 异步写入文件
            await File.WriteAllTextAsync(_dataFilePath, json);
            
            Console.WriteLine($"[DataPersistenceService] ✅ Data saved successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DataPersistenceService] ❌ Error saving data: {ex.Message}");
            return false;
        }
    }

    public async Task<AppData> LoadDataAsync()
    {
        try
        {
            if (!DataFileExists())
            {
                Console.WriteLine($"[DataPersistenceService] Data file does not exist, returning default data");
                return new AppData();
            }

            Console.WriteLine($"[DataPersistenceService] Loading data from: {_dataFilePath}");
            
            // 异步读取文件
            var json = await File.ReadAllTextAsync(_dataFilePath);
            
            // 反序列化 JSON
            var data = JsonSerializer.Deserialize<AppData>(json, _jsonOptions);
            
            if (data == null)
            {
                Console.WriteLine($"[DataPersistenceService] ❌ Failed to deserialize data, returning default");
                return new AppData();
            }
            
            Console.WriteLine($"[DataPersistenceService] ✅ Data loaded successfully - Tasks: {data.RenderQueue.Count}, Blender: {data.Settings.BlenderPath}, FFmpeg: {data.Settings.FfmpegPath}");
            return data;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DataPersistenceService] ❌ Error loading data: {ex.Message}");
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
                Console.WriteLine($"[DataPersistenceService] ✅ Data file deleted: {_dataFilePath}");
                return true;
            }
            return true; // 文件不存在也算删除成功
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DataPersistenceService] ❌ Error deleting data file: {ex.Message}");
            return false;
        }
    }
}
