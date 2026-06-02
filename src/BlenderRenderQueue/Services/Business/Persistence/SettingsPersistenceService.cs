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
        var operation = _logService.BeginOperation(
            RenderLogScope.Recovery,
            "SaveSettings",
            nameof(SettingsPersistenceService),
            "开始保存设置。",
            metadata: new Dictionary<string, string>
            {
                ["path"] = SettingsFilePath
            });
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

            operation.Detail($"保存设置到: {SettingsFilePath}");

            // 序列化并保存到文件
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.SettingsData);
            operation.Detail(
                $"Serialized JSON: {json}",
                RenderLogLevel.Debug,
                new Dictionary<string, string>
                {
                    ["bytes"] = json.Length.ToString()
                });
            await File.WriteAllTextAsync(SettingsFilePath, json);

            operation.Complete(
                $"设置保存完成，默认超时: {settings.DefaultRenderTimeoutSeconds}s",
                audience: RenderLogMetadata.AudienceDiagnostic,
                metadata: new Dictionary<string, string>
                {
                    ["selected_blender"] = settings.SelectedBlenderPath,
                    ["timeout_seconds"] = settings.DefaultRenderTimeoutSeconds.ToString()
                });
            return true;
        }
        catch (Exception ex)
        {
            operation.Fail($"设置保存失败: {ex.Message}");
            return false;
        }
    }

    public async Task<SettingsData> LoadSettingsAsync()
    {
        var operation = _logService.BeginOperation(
            RenderLogScope.Recovery,
            "LoadSettings",
            nameof(SettingsPersistenceService),
            "开始读取设置。",
            metadata: new Dictionary<string, string>
            {
                ["path"] = SettingsFilePath
            });
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                operation.Complete(
                    "未找到设置文件，使用默认设置。",
                    RenderLogLevel.Warning,
                    audience: RenderLogMetadata.AudienceDiagnostic);
                return new SettingsData();
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath);
            operation.Detail(
                $"Raw JSON content: {json}",
                RenderLogLevel.Debug,
                new Dictionary<string, string>
                {
                    ["bytes"] = json.Length.ToString()
                });
            
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData);

            if (settings == null)
            {
                operation.Fail("设置反序列化失败，使用默认设置。");
                return new SettingsData();
            }

            // 版本兼容性检查
            if (string.IsNullOrEmpty(settings.Software) || settings.Software != "BlenderRenderQueue")
            {
                operation.Fail("设置文件软件标识无效，使用默认设置。");
                return new SettingsData();
            }

            // 版本检查（如果需要，可以在这里添加版本迁移逻辑）
            if (string.IsNullOrEmpty(settings.Version))
            {
                operation.Detail("设置文件没有版本号，按旧格式处理。", RenderLogLevel.Warning);
                settings.Version = "0.0.1"; // 设置默认版本
            }

            operation.Complete(
                $"设置读取完成，默认超时: {settings.DefaultRenderTimeoutSeconds}s",
                audience: RenderLogMetadata.AudienceDiagnostic,
                metadata: new Dictionary<string, string>
                {
                    ["version"] = settings.Version,
                    ["selected_blender"] = settings.SelectedBlenderPath,
                    ["timeout_seconds"] = settings.DefaultRenderTimeoutSeconds.ToString()
                });
            return settings;
        }
        catch (Exception ex)
        {
            operation.Fail($"设置读取失败: {ex.Message}");
            return new SettingsData();
        }
    }
}
