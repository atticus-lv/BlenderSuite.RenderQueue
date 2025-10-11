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
using BlenderRenderQueue.Helpers;

namespace BlenderRenderQueue.Services.Business.Api;

public class RenderQueueApiService : IRenderQueueApiService, IDisposable
{
    private readonly RenderQueueViewModel _renderQueue;
    private WebApplication? _app;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentQueue<OptimizedProgressUpdate> _progressUpdates = new();
    private readonly Lock _progressLock = new();
    private bool _disposed;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    public event EventHandler<ApiServiceStatusChangedEventArgs>? StatusChanged;

    public RenderQueueApiService(RenderQueueViewModel renderQueue)
    {
        _renderQueue = renderQueue;

        foreach (var task in _renderQueue.RenderTasks)
        {
            task.ProgressChanged += OnTaskProgressChanged;
        }

        _renderQueue.RenderTasks.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (RenderTaskViewModel newTask in e.NewItems)
                {
                    newTask.ProgressChanged += OnTaskProgressChanged;
                }
            }

            if (e.OldItems == null) return;
            foreach (RenderTaskViewModel oldTask in e.OldItems)
            {
                oldTask.ProgressChanged -= OnTaskProgressChanged;
            }
        };
    }

    private void OnTaskProgressChanged(object? sender, RenderTaskProgressEventArgs e)
    {
        if (sender is not RenderTaskViewModel task) return;

        var update = new OptimizedProgressUpdate
        {
            Timestamp = DateTime.UtcNow
        };

        if (task == _renderQueue.CurrentRenderingTask)
        {
            update.CurrentTask = task.ToCurrentTaskProgress();
        }
        else
        {
            update.StatusChanges = [task.ToTaskStatusChange()];
        }

        lock (_progressLock)
        {
            _progressUpdates.Enqueue(update);

            while (_progressUpdates.Count > 1000)
            {
                _progressUpdates.TryDequeue(out _);
            }
        }
    }

    public async Task StartAsync(int port = 8325)
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

            builder.Services.AddCors();

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.WriteIndented = true;
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.Encoder =
                    System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                options.SerializerOptions.TypeInfoResolver = ApiJsonContext.Default;
            });

            _app = builder.Build();

            _app.UseCors(policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());

            _app.MapGet("/api/queue/status", () =>
            {
                // Console.WriteLine("[RenderQueueApiService] Received queue status request");
                try
                {
                    var response = new OptimizedQueueStatusResponse
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
                        Tasks = _renderQueue.RenderTasks.Select(task => task.ToOptimizedTaskInfo()).ToList()
                    };

                    // Console.WriteLine(
                    //     $"[RenderQueueApiService] 📊 Returning optimized queue status: {response.QueueState}, Progress: {response.OverallProgress:P1}, Tasks: {response.ActiveTaskCount}");
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    // Console.WriteLine($"[RenderQueueApiService] Queue status error: {ex.Message}");
                    return Results.Problem($"Queue status failed: {ex.Message}");
                }
            });

            _app.MapGet("/api/queue/tasks", () =>
            {
                try
                {
                    var tasks = _renderQueue.RenderTasks.Select(task => task.ToOptimizedTaskInfo()).ToList();
                    return Results.Ok(tasks);
                }
                catch (Exception ex)
                {
                    // Console.WriteLine($"[RenderQueueApiService] ❌ Tasks list error: {ex.Message}");
                    return Results.Problem($"Tasks list failed: {ex.Message}");
                }
            });

            // 实时进度更新流API (Server-Sent Events) - 使用优化的推送模型
            _app.MapGet("/api/queue/progress-stream", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Headers.Connection = "keep-alive";
                context.Response.Headers.AccessControlAllowOrigin = "*";

                var lastUpdateCount = 0;

                while (!context.RequestAborted.IsCancellationRequested)
                {
                    var currentUpdates = new List<OptimizedProgressUpdate>();

                    lock (_progressLock)
                    {
                        var updates = _progressUpdates.ToArray();
                        if (updates.Length > lastUpdateCount)
                        {
                            currentUpdates = updates.Skip(lastUpdateCount).ToList();
                            lastUpdateCount = updates.Length;
                        }
                    }

                    if (currentUpdates.Count != 0)
                    {
                        var options = new JsonSerializerOptions
                        {
                            TypeInfoResolver = ApiJsonContext.Default
                        };
                        var json = JsonSerializer.Serialize(currentUpdates, options);
                        await context.Response.WriteAsync($"data: {json}\n\n");
                        await context.Response.Body.FlushAsync();
                    }

                    await Task.Delay(1000); // Check for updates every second
                }
            });

            _app.MapGet("/api/queue/task/{taskId}/progress", (int taskId) =>
            {
                lock (_progressLock)
                {
                    return _progressUpdates
                        .Where(u => (u.CurrentTask?.TaskId == taskId) ||
                                    (u.StatusChanges?.Any(s => s.TaskId == taskId) == true))
                        .OrderBy(u => u.Timestamp)
                        .Take(100) // 最近100条记录
                        .ToList();
                }
            });


            _app.MapGet("/api/health", () =>
            {
                // Console.WriteLine($"[RenderQueueApiService] Received health check request");
                try
                {
                    var response = new HealthResponse
                    {
                        Status = "healthy",
                        Timestamp = DateTime.UtcNow,
                        Version = "1.0.0"
                    };
                    // Console.WriteLine($"[RenderQueueApiService] Returning health status: {response.Status}");
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    // Console.WriteLine($"[RenderQueueApiService] ❌ Health check error: {ex.Message}");
                    return Results.Problem($"Health check failed: {ex.Message}");
                }
            });

            _cancellationTokenSource = new CancellationTokenSource();
            IsRunning = true;

            var localNetworkIp = NetworkHelper.GetLocalNetworkIpAddress();

            Console.WriteLine($"[RenderQueueApiService] API service started successfully!");
            Console.WriteLine($"[RenderQueueApiService] Listening on: http://*:{port}");
            Console.WriteLine($"[RenderQueueApiService] Local access: http://localhost:{port}");
            Console.WriteLine($"[RenderQueueApiService] Network access: http://{localNetworkIp}:{port}");
            Console.WriteLine($"[RenderQueueApiService] Available endpoints:");
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
            Console.WriteLine($"[RenderQueueApiService] Stopping API services, ports: {Port}");

            // 取消所有正在进行的操作
            _cancellationTokenSource?.Cancel();

            if (_app != null)
            {
                Console.WriteLine($"[RenderQueueApiService] IS STOPPING WebApplication...");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                try
                {
                    await _app.StopAsync(cts.Token);
                }
                catch (OperationCanceledException)
                { }

                await _app.DisposeAsync();
                _app = null;
            }

            IsRunning = false;
            StatusChanged?.Invoke(this,
                new ApiServiceStatusChangedEventArgs(false, Port, "The API service has been discontinued"));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this,
                new ApiServiceStatusChangedEventArgs(false, Port, $"API service stop failed: {ex.Message}"));
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
            StopAsync().Wait(10000); // 等待最多10秒
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[RenderQueueApiService] An exception occurs when releasing an API service resource: {ex.Message}");
        }

        _cancellationTokenSource?.Dispose();
        _disposed = true;
    }
}