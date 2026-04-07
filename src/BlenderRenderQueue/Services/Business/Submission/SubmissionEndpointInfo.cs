using System;
using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Services.Business.Submission;

public sealed class SubmissionEndpointInfo
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("host")]
    public string Host { get; init; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("app_instance_id")]
    public string AppInstanceId { get; init; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
}
