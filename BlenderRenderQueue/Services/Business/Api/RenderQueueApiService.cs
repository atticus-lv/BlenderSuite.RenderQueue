using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using BlenderRenderQueue.Models;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Services.Business.Api.Models;

namespace BlenderRenderQueue.Services.Business.Api;

/// <summary>
/// 渲染队列API服务实现
/// </summary>
public class RenderQueueApiService : IRenderQueueApiService, IDisposable
{
    private readonly RenderQueueViewModel _renderQueue;
    private WebApplication? _app;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<ProgressUpdate> _progressUpdates = new();
    private readonly object _progressLock = new();
    private bool _disposed;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    public event EventHandler<ApiServiceStatusChangedEventArgs>? StatusChanged;

    public RenderQueueApiService(RenderQueueViewModel renderQueue)
    {
        _renderQueue = renderQueue;

        // 订阅所有现有任务的进度变化事件
        foreach (var task in _renderQueue.RenderTasks)
        {
            task.ProgressChanged += OnTaskProgressChanged;
        }

        // 订阅队列任务集合变化，自动订阅新任务的进度事件
        _renderQueue.RenderTasks.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (RenderTaskViewModel newTask in e.NewItems)
                {
                    newTask.ProgressChanged += OnTaskProgressChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (RenderTaskViewModel oldTask in e.OldItems)
                {
                    oldTask.ProgressChanged -= OnTaskProgressChanged;
                }
            }
        };
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        if (sender is not RenderTaskViewModel task) return;
        var update = task.ToProgressUpdate();
        update.Timestamp = DateTime.UtcNow;

        lock (_progressLock)
        {
            _progressUpdates.Enqueue(update);

            while (_progressUpdates.Count > 1000)
            {
                _progressUpdates.TryDequeue(out _);
            }
        }
    }

    public async Task StartAsync(int port = 8080)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("API服务已在运行中");
        }

        try
        {
            Port = port;

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://*:{port}");

            // 添加CORS服务
            builder.Services.AddCors();

            _app = builder.Build();

            // 配置CORS
            _app.UseCors(policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());

            // 获取队列状态API
            _app.MapGet("/api/queue/status", () =>
            {
                Console.WriteLine($"[RenderQueueApiService] 📊 Received queue status request");
                var currentTask = _renderQueue.CurrentRenderingTask;
                var currentTaskInfo = currentTask?.ToCurrentTaskInfo();

                var response = new QueueStatusResponse
                {
                    Timestamp = DateTime.UtcNow,
                    QueueState = _renderQueue.QueueState,
                    ActiveTaskCount = _renderQueue.ActiveTaskCount,
                    CompletedTaskCount = _renderQueue.CompletedTaskCount,
                    FailedTaskCount = _renderQueue.FailedTaskCount,
                    TotalFrames = _renderQueue.TotalFrames,
                    CompletedFrames = _renderQueue.CompletedFrames,
                    OverallProgress = _renderQueue.OverallQueueProgress,
                    RemainingTime = _renderQueue.RemainingTimeText,
                    CurrentTask = currentTaskInfo
                };

                Console.WriteLine(
                    $"[RenderQueueApiService] 📊 Returning queue status: {response.QueueState}, Progress: {response.OverallProgress:P1}, Tasks: {response.ActiveTaskCount}");
                return response;
            });

            // 获取所有任务列表API
            _app.MapGet("/api/queue/tasks", () => _renderQueue.RenderTasks.Select(task => task.ToApiResponse()));

            // 实时进度更新流API (Server-Sent Events)
            _app.MapGet("/api/queue/progress-stream", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["Connection"] = "keep-alive";
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";

                var lastUpdateCount = 0;

                while (!context.RequestAborted.IsCancellationRequested)
                {
                    var currentUpdates = new List<ProgressUpdate>();

                    lock (_progressLock)
                    {
                        var updates = _progressUpdates.ToArray();
                        if (updates.Length > lastUpdateCount)
                        {
                            currentUpdates = updates.Skip(lastUpdateCount).ToList();
                            lastUpdateCount = updates.Length;
                        }
                    }

                    if (currentUpdates.Any())
                    {
                        var json = JsonSerializer.Serialize(currentUpdates, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });
                        await context.Response.WriteAsync($"data: {json}\n\n");
                        await context.Response.Body.FlushAsync();
                    }

                    await Task.Delay(1000); // 每秒检查一次更新
                }
            });

            // 特定任务进度历史API
            _app.MapGet("/api/queue/task/{taskId}/progress", (int taskId) =>
            {
                lock (_progressLock)
                {
                    return _progressUpdates
                        .Where(u => u.TaskId == taskId)
                        .OrderBy(u => u.Timestamp)
                        .Take(100) // 最近100条记录
                        .ToList();
                }
            });

            // 健康检查API
            _app.MapGet("/api/health", () =>
            {
                Console.WriteLine($"[RenderQueueApiService] ❤️ Received health check request");
                var response = new { status = "healthy", timestamp = DateTime.UtcNow };
                Console.WriteLine($"[RenderQueueApiService] ❤️ Returning health status: {response.status}");
                return response;
            });

            _cancellationTokenSource = new CancellationTokenSource();
            IsRunning = true;

            Console.WriteLine($"[RenderQueueApiService] 🚀 API service started successfully!");
            Console.WriteLine($"[RenderQueueApiService] 📡 Listening on: http://*:{port}");
            Console.WriteLine($"[RenderQueueApiService] 🌐 Local access: http://localhost:{port}");
            Console.WriteLine($"[RenderQueueApiService] 📋 Available endpoints:");
            Console.WriteLine($"[RenderQueueApiService]   - GET /api/health");
            Console.WriteLine($"[RenderQueueApiService]   - GET /api/queue/status");
            Console.WriteLine($"[RenderQueueApiService]   - GET /api/queue/tasks");
            Console.WriteLine($"[RenderQueueApiService]   - GET /api/queue/progress-stream");
            Console.WriteLine($"[RenderQueueApiService]   - GET /api/queue/task/{{taskId}}/progress");

            StatusChanged?.Invoke(this, new ApiServiceStatusChangedEventArgs(true, port, $"API服务已启动，端口: {port}"));

            await _app.RunAsync();
        }
        catch (Exception ex)
        {
            IsRunning = false;
            StatusChanged?.Invoke(this, new ApiServiceStatusChangedEventArgs(false, port, $"API服务启动失败: {ex.Message}"));
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _cancellationTokenSource?.Cancel();

            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _app = null;
            }

            IsRunning = false;
            StatusChanged?.Invoke(this, new ApiServiceStatusChangedEventArgs(false, Port, "API服务已停止"));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, new ApiServiceStatusChangedEventArgs(false, Port, $"API服务停止失败: {ex.Message}"));
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            StopAsync().Wait(5000); // 等待最多5秒
        }
        catch
        {
            // 忽略停止时的异常
        }

        _cancellationTokenSource?.Dispose();
        _disposed = true;
    }
}