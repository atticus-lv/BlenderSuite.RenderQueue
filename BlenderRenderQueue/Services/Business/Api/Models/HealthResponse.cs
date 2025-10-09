using System;
using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Services.Business.Api.Models;

/// <summary>
/// API健康检查响应模型
/// </summary>
public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}
