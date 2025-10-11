using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;

namespace BlenderSuite.RenderQueue.Services;

/// <summary>
/// JSON序列化上下文，支持AOT模式
/// </summary>
[JsonSerializable(typeof(QueueClientData))]
public partial class QueueClientJsonContext : JsonSerializerContext
{
}

/// <summary>
/// 队列客户端数据持久化服务
/// </summary>
public class QueueClientPersistenceService
{
    private static readonly string DataFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlenderRenderQueue",
        "client_data.json"
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 使用预构建的JsonSerializerContext来支持AOT模式
        TypeInfoResolver = QueueClientJsonContext.Default
    };

    /// <summary>
    /// 保存客户端数据
    /// </summary>
    /// <param name="data">要保存的数据</param>
    /// <returns>保存是否成功</returns>
    public async Task<bool> SaveDataAsync(QueueClientData data)
    {
        try
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(DataFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 确保版本信息正确
            data.Software = "QueueClient";
            data.Version = "1.0.0";
            data.LastUpdated = DateTime.Now;

            Console.WriteLine($"[QueueClientPersistenceService] Save data to: {DataFilePath}");

            // 序列化并保存到文件
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(DataFilePath, json);

            Console.WriteLine($"[QueueClientPersistenceService] Data Saved Successfully - Number of Servers: {data.ServerUrls.Count}, 刷新间隔: {data.RefreshInterval}s, 自动刷新: {data.AutoRefresh}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QueueClientPersistenceService]  Failed to save data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 加载客户端数据
    /// </summary>
    /// <returns>加载的数据，如果失败则返回默认数据</returns>
    public async Task<QueueClientData> LoadDataAsync()
    {
        try
        {
            if (!File.Exists(DataFilePath))
            {
                Console.WriteLine($"[QueueClientPersistenceService] The data file does not exist, using the default data: {DataFilePath}");
                return new QueueClientData();
            }

            Console.WriteLine($"[QueueClientPersistenceService] Loading data from a file: {DataFilePath}");

            var json = await File.ReadAllTextAsync(DataFilePath);
            var data = JsonSerializer.Deserialize<QueueClientData>(json, JsonOptions);

            if (data == null)
            {
                Console.WriteLine($"[QueueClientPersistenceService] deserialization fails, using default data");
                return new QueueClientData();
            }

            // 版本兼容性检查
            if (string.IsNullOrEmpty(data.Software) || data.Software != "QueueClient")
            {
                Console.WriteLine($"[QueueClientPersistenceService] ⚠ Invalid software identifier, using default data");
                return new QueueClientData();
            }

            Console.WriteLine($"[QueueClientPersistenceService] Data Loading Success - Number of servers: {data.ServerUrls.Count}, 刷新间隔: {data.RefreshInterval}s, 自动刷新: {data.AutoRefresh}");
            return data;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QueueClientPersistenceService]  Failed to load data: {ex.Message}");
            return new QueueClientData();
        }
    }

    /// <summary>
    /// 检查数据文件是否存在
    /// </summary>
    /// <returns>文件是否存在</returns>
    public bool DataFileExists()
    {
        return File.Exists(DataFilePath);
    }

    /// <summary>
    /// 删除数据文件
    /// </summary>
    /// <returns>删除是否成功</returns>
    public bool DeleteDataFile()
    {
        try
        {
            if (DataFileExists())
            {
                File.Delete(DataFilePath);
                Console.WriteLine($"[QueueClientPersistenceService] ✅ 数据文件已删除: {DataFilePath}");
                return true;
            }

            return true; // 文件不存在也算删除成功
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QueueClientPersistenceService] ❌ 删除数据文件失败: {ex.Message}");
            return false;
        }
    }
}
