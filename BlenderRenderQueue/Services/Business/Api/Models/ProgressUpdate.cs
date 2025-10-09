using System;
using System.Text.Json.Serialization;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.Services.Business.Api.Models;

public class ProgressUpdate
{
    [JsonPropertyName("taskId")]
    public int TaskId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public RenderTaskStatus Status { get; set; }

    [JsonPropertyName("progress")]
    public RenderProgress Progress { get; set; } = new();

    [JsonPropertyName("overallProgress")]
    public double OverallProgress { get; set; }

    [JsonPropertyName("currentFrameProgress")]
    public double CurrentFrameProgress { get; set; }
}