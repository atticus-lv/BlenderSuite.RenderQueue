using System.Collections.Generic;
using System.Text.Json.Serialization;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.Services.Business.Api.Models;

namespace BlenderRenderQueue.Services.Business.Api;

/// <summary>
/// AOT兼容的JSON序列化上下文
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(QueueStatusResponse))]
[JsonSerializable(typeof(CurrentTaskInfo))]
[JsonSerializable(typeof(TaskInfoResponse))]
[JsonSerializable(typeof(ProgressUpdate))]
[JsonSerializable(typeof(RenderProgress))]
[JsonSerializable(typeof(RenderTaskStatus))]
[JsonSerializable(typeof(QueueState))]
[JsonSerializable(typeof(RenderEngine))]
[JsonSerializable(typeof(List<TaskInfoResponse>))]
[JsonSerializable(typeof(List<ProgressUpdate>), TypeInfoPropertyName = "ListProgressUpdate")]

// 新增的优化模型
[JsonSerializable(typeof(OptimizedQueueStatusResponse))]
[JsonSerializable(typeof(OptimizedTaskInfo))]
[JsonSerializable(typeof(OptimizedProgressUpdate))]
[JsonSerializable(typeof(CurrentTaskProgress))]
[JsonSerializable(typeof(RealtimeRenderProgress))]
[JsonSerializable(typeof(TaskStatusChange))]
[JsonSerializable(typeof(List<OptimizedTaskInfo>))]
[JsonSerializable(typeof(List<TaskStatusChange>))]
[JsonSerializable(typeof(List<OptimizedProgressUpdate>))]
public partial class ApiJsonContext : JsonSerializerContext
{
}
