using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Persistence;

/// <summary>
/// JSON serialization context supports AOT mode
/// </summary>
[JsonSerializable(typeof(SettingsData))]
[JsonSerializable(typeof(BlenderExecutable))]
[JsonSerializable(typeof(List<BlenderExecutable>), TypeInfoPropertyName = "BlenderExecutableList")]
public partial class SettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// 设置数据持久化服务实现
/// </summary>
public class SettingsPersistenceService : ISettingsPersistenceService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlenderRenderQueue",
        "settings.json"
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Remove PropertyNamingPolicy，use JsonPropertyName to control the naming
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Use the pre-built JsonSerializerContext to support AOT mode
        TypeInfoResolver = SettingsJsonContext.Default
    };

    public async Task<bool> SaveSettingsAsync(SettingsData settings)
    {
        try
        {
            // 确保目录存在
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 确保版本信息正确
            settings.Software = "BlenderRenderQueue";
            settings.Version = "0.0.1";

            Console.WriteLine($"[SettingsPersistenceService] Saving settings to: {SettingsFilePath}");

            // 序列化并保存到文件
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.SettingsData);
            Console.WriteLine($"[SettingsPersistenceService] Serialized JSON: {json}");
            await File.WriteAllTextAsync(SettingsFilePath, json);

            Console.WriteLine($"[SettingsPersistenceService] ✅ Settings saved successfully - Selected Blender: {settings.SelectedBlenderPath}, Timeout: {settings.DefaultRenderTimeoutSeconds}s");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsPersistenceService] ❌ Failed to save settings: {ex.Message}");
            return false;
        }
    }

    public async Task<SettingsData> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                Console.WriteLine($"[SettingsPersistenceService] Settings file not found, using defaults: {SettingsFilePath}");
                return new SettingsData();
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath);
            Console.WriteLine($"[SettingsPersistenceService] Raw JSON content: {json}");
            
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData);

            if (settings == null)
            {
                Console.WriteLine($"[SettingsPersistenceService] Failed to deserialize settings, using defaults");
                return new SettingsData();
            }

            // 版本兼容性检查
            if (string.IsNullOrEmpty(settings.Software) || settings.Software != "BlenderRenderQueue")
            {
                Console.WriteLine($"[SettingsPersistenceService] ⚠️ Invalid software identifier, using defaults");
                return new SettingsData();
            }

            // 版本检查（如果需要，可以在这里添加版本迁移逻辑）
            if (string.IsNullOrEmpty(settings.Version))
            {
                Console.WriteLine($"[SettingsPersistenceService] ⚠️ No version found, assuming legacy format");
                settings.Version = "0.0.1"; // 设置默认版本
            }

            Console.WriteLine($"[SettingsPersistenceService] ✅ Settings loaded successfully - Version: {settings.Version}, Selected Blender: {settings.SelectedBlenderPath}, Timeout: {settings.DefaultRenderTimeoutSeconds}s");
            return settings;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsPersistenceService] ❌ Failed to load settings: {ex.Message}");
            return new SettingsData();
        }
    }
}
