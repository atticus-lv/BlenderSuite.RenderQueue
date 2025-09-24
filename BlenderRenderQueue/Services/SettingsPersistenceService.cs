using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services;

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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 使用 DefaultJsonTypeInfoResolver 来支持反射序列化，同时保持 AOT 兼容性
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
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
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(SettingsFilePath, json);

            Console.WriteLine($"[SettingsPersistenceService] ✅ Settings saved successfully - Blender: {settings.BlenderPath}, FFmpeg: {settings.FfmpegPath}, Timeout: {settings.DefaultRenderTimeoutSeconds}s");
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
            var settings = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions);

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

            Console.WriteLine($"[SettingsPersistenceService] ✅ Settings loaded successfully - Version: {settings.Version}, Blender: {settings.BlenderPath}, FFmpeg: {settings.FfmpegPath}, Timeout: {settings.DefaultRenderTimeoutSeconds}s");
            return settings;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsPersistenceService] ❌ Failed to load settings: {ex.Message}");
            return new SettingsData();
        }
    }
}
