using System;
using System.Text.Json.Serialization;

namespace BlenderSuite.RenderQueue.Services.Business.Submission;

public sealed class LocalSubmissionRequest
{
    [JsonPropertyName("filepath")]
    public string Filepath { get; init; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; init; } = string.Empty;

    [JsonPropertyName("scene_name")]
    public string SceneName { get; init; } = string.Empty;

    [JsonPropertyName("override_frame_range")]
    public bool OverrideFrameRange { get; init; }

    [JsonPropertyName("frame_start")]
    public int FrameStart { get; init; } = 1;

    [JsonPropertyName("frame_end")]
    public int FrameEnd { get; init; } = 1;

    [JsonPropertyName("submitted_at")]
    public string SubmittedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
}
