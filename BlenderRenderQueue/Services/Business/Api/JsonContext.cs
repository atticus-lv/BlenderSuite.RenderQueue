using System.Collections.Generic;
using System.Text.Json.Serialization;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Api.Models;

namespace BlenderRenderQueue.Services.Business.Api;

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(RenderProgress))]
[JsonSerializable(typeof(RenderTaskStatus))]
[JsonSerializable(typeof(QueueState))]
[JsonSerializable(typeof(RenderEngine))]
[JsonSerializable(typeof(OptimizedQueueStatusResponse))]
[JsonSerializable(typeof(OptimizedTaskInfo))]
[JsonSerializable(typeof(OptimizedProgressUpdate))]
[JsonSerializable(typeof(CurrentTaskProgress))]
[JsonSerializable(typeof(RealtimeRenderProgress))]
[JsonSerializable(typeof(TaskStatusChange))]
[JsonSerializable(typeof(List<OptimizedTaskInfo>))]
[JsonSerializable(typeof(List<TaskStatusChange>))]
[JsonSerializable(typeof(List<OptimizedProgressUpdate>))]
public partial class ApiJsonContext : JsonSerializerContext;