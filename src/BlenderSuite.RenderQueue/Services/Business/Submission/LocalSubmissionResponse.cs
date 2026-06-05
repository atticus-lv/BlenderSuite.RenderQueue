using System.Text.Json.Serialization;

namespace BlenderSuite.RenderQueue.Services.Business.Submission;

public sealed class LocalSubmissionResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("task_id")]
    public string TaskId { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("queue_state")]
    public string QueueState { get; init; } = string.Empty;
}
