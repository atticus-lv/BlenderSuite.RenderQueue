using System.Text.Json.Serialization;

namespace BlenderSuite.RenderQueue.Models;

/// <summary>
/// 视频编码选项模型
/// </summary>
public class VideoCodecOption
{
    [JsonPropertyName("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;

    public VideoCodecOption()
    {
    }

    public VideoCodecOption(string displayName, string value)
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
        if (obj is VideoCodecOption other)
        {
            return Value == other.Value;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    // 预定义的编码选项
    public static readonly VideoCodecOption H264 = new("H.264", "H264");
    public static readonly VideoCodecOption H265 = new("H.265", "H265");
    public static readonly VideoCodecOption AV1 = new("AV1", "AV1");

    public static readonly VideoCodecOption[] AllOptions = { H264, H265, AV1 };
}
