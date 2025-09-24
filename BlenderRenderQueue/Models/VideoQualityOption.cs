using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Models;

/// <summary>
/// 视频质量选项模型
/// </summary>
public class VideoQualityOption
{
    [JsonPropertyName("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;

    public VideoQualityOption()
    {
    }

    public VideoQualityOption(string displayName, string value)
    {
        DisplayName = displayName;
        Value = value;
    }

    public override string ToString()
    {
        return DisplayName;
    }

    public override bool Equals(object? obj)
    {
        if (obj is VideoQualityOption other)
        {
            return Value == other.Value;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    // 预定义的质量选项
    public static readonly VideoQualityOption Lossless = new("无损", "LOSSLESS");
    public static readonly VideoQualityOption PerceptualLossless = new("感知无损", "PERC_LOSSLESS");
    public static readonly VideoQualityOption High = new("高质量", "HIGH");
    public static readonly VideoQualityOption Medium = new("中等质量", "MEDIUM");
    public static readonly VideoQualityOption Low = new("低质量", "LOW");

    public static readonly VideoQualityOption[] AllOptions = { Lossless, PerceptualLossless, High, Medium, Low };
}
