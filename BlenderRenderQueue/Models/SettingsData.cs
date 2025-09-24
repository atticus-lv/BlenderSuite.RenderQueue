using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Models;

/// <summary>
/// 设置数据模型 - 独立存储到 settings.json
/// </summary>
public class SettingsData
{
    [JsonPropertyName("Software")]
    public string Software { get; set; } = "BlenderRenderQueue";

    [JsonPropertyName("Version")]
    public string Version { get; set; } = "0.0.1";

    [JsonPropertyName("BlenderPath")]
    public string BlenderPath { get; set; } = string.Empty;

    [JsonPropertyName("FfmpegPath")]
    public string FfmpegPath { get; set; } = string.Empty;

    [JsonPropertyName("DefaultRenderTimeoutSeconds")]
    public int DefaultRenderTimeoutSeconds { get; set; } = 300; // 默认五分钟

    [JsonPropertyName("MaxRetryAttempts")]
    public int MaxRetryAttempts { get; set; } = 3; // 默认最大重试3次

    [JsonPropertyName("VideoGenerationMethod")]
    public string VideoGenerationMethod { get; set; } = "Blender"; // 默认使用Blender生成视频

    [JsonPropertyName("VideoCodec")]
    public string VideoCodec { get; set; } = "H264"; // 默认使用H264编码
}
