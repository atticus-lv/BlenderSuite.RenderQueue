using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Application.Logging;

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
    private readonly IRenderLogService _logService;

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

    public SettingsPersistenceService(IRenderLogService logService)
    {
        _logService = logService;
    }

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

            _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"Saving settings to: {SettingsFilePath}", source: "SettingsPersistenceService");

            // 序列化并保存到文件
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.SettingsData);
            _logService.Write(RenderLogLevel.Info, RenderLogScope.Recovery, $"Serialized JSON: {json}", source: "SettingsPersistenceService");
            await File.WriteAllTextAsync(SettingsFilePath, json);

            _logService.Write(RenderLogLevel.Warning, RenderLogScope.Recovery, $"✅ Settings saved successfully - Selected Blender: {settings.SelectedBlenderPath}, Timeout: {settings.DefaultRenderTimeoutSeconds}s", source: "SettingsPersistenceService");
            return true;
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"❌ Failed to save settings: {ex.Message}", source: "SettingsPersistenceService");
            return false;
        }
    }

    public async Task<SettingsData> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                _logService.Write(RenderLogLevel.Warning, RenderLogScope.Recovery, $"Settings file not found, using defaults: {SettingsFilePath}", source: "SettingsPersistenceService");
                return new SettingsData();
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath);
            _logService.Write(RenderLogLevel.Debug, RenderLogScope.Recovery, $"Raw JSON content: {json}", source: "SettingsPersistenceService");
            
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData);

            if (settings == null)
            {
                _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"Failed to deserialize settings, using defaults", source: "SettingsPersistenceService");
                return new SettingsData();
            }

            // 版本兼容性检查
            if (string.IsNullOrEmpty(settings.Software) || settings.Software != "BlenderRenderQueue")
            {
                _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"⚠️ Invalid software identifier, using defaults", source: "SettingsPersistenceService");
                return new SettingsData();
            }

            // 版本检查（如果需要，可以在这里添加版本迁移逻辑）
            if (string.IsNullOrEmpty(settings.Version))
            {
                _logService.Write(RenderLogLevel.Warning, RenderLogScope.Recovery, $"⚠️ No version found, assuming legacy format", source: "SettingsPersistenceService");
                settings.Version = "0.0.1"; // 设置默认版本
            }

            _logService.Write(RenderLogLevel.Warning, RenderLogScope.Recovery, $"✅ Settings loaded successfully - Version: {settings.Version}, Selected Blender: {settings.SelectedBlenderPath}, Timeout: {settings.DefaultRenderTimeoutSeconds}s", source: "SettingsPersistenceService");
            return settings;
        }
        catch (Exception ex)
        {
            _logService.Write(RenderLogLevel.Error, RenderLogScope.Recovery, $"❌ Failed to load settings: {ex.Message}", source: "SettingsPersistenceService");
            return new SettingsData();
        }
    }
}
