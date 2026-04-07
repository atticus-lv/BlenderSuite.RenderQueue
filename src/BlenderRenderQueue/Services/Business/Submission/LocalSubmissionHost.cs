using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Application.Logging;
using BlenderRenderQueue.Services.Application.Queue;

namespace BlenderRenderQueue.Services.Business.Submission;

[JsonSerializable(typeof(SubmissionWireRequest))]
[JsonSerializable(typeof(SubmissionWireResponse))]
[JsonSerializable(typeof(SubmissionEndpointInfo))]
[JsonSerializable(typeof(LocalSubmissionRequest))]
internal partial class SubmissionJsonContext : JsonSerializerContext
{
}

internal sealed class SubmissionWireRequest
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}

internal sealed class SubmissionWireResponse
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("task_id")]
    public string TaskId { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("queue_state")]
    public string QueueState { get; init; } = string.Empty;
}

public sealed class LocalSubmissionHost : ILocalSubmissionHost
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly IRenderQueueApplicationService _queueApplicationService;
    private readonly IRenderLogService _logService;
    private readonly object _clientTasksLock = new();
    private readonly HashSet<Task> _clientTasks = [];
    private CancellationTokenSource? _hostCts;
    private TcpListener? _listener;
    private Thread? _acceptLoopThread;
    private bool _disposed;

    public LocalSubmissionHost(IRenderQueueApplicationService queueApplicationService, IRenderLogService logService)
    {
        _queueApplicationService = queueApplicationService;
        _logService = logService;
    }

    public SubmissionEndpointInfo? CurrentEndpoint { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();

            if (_listener != null)
            {
                return;
            }

            _hostCts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            CurrentEndpoint = new SubmissionEndpointInfo
            {
                Host = "127.0.0.1",
                Port = endpoint.Port,
                Token = Guid.NewGuid().ToString("N"),
                AppInstanceId = Guid.NewGuid().ToString("N"),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
            };

            _acceptLoopThread = new Thread(() => AcceptLoop(_hostCts.Token))
            {
                IsBackground = true,
                Name = "BRQ Local Submission Host"
            };
            _acceptLoopThread.Start();

            await ProbeReadyAsync(cancellationToken);
            await WriteEndpointFileAsync(CurrentEndpoint, cancellationToken);
            Console.WriteLine($"[LocalSubmissionHost] Listening on {CurrentEndpoint.Host}:{CurrentEndpoint.Port}");
            _logService.Write(
                RenderLogLevel.Info,
                RenderLogScope.Submission,
                $"本地 submission host 已启动: {CurrentEndpoint.Host}:{CurrentEndpoint.Port}",
                source: nameof(LocalSubmissionHost));
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            _hostCts?.Cancel();

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                    // ignored
                }
            }

            if (_acceptLoopThread != null)
            {
                try
                {
                    _acceptLoopThread.Join(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // ignored
                }
            }

            _listener = null;
            _acceptLoopThread = null;
            await WaitForClientTasksAsync(cancellationToken);
            _hostCts?.Dispose();
            _hostCts = null;
            CurrentEndpoint = null;

            DeleteEndpointFile();
            _logService.Write(RenderLogLevel.Info, RenderLogScope.Submission, "本地 submission host 已停止。", source: nameof(LocalSubmissionHost));
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // ignored
        }
        finally
        {
            _lifecycleLock.Dispose();
        }
    }

    private void AcceptLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            TcpClient? client = null;
            try
            {
                client = _listener.AcceptTcpClient();
                TrackClientTask(HandleClientAsync(client, cancellationToken));
                client = null;
            }
            catch (ObjectDisposedException)
            {
                client?.Dispose();
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                client?.Dispose();
                Console.WriteLine($"[LocalSubmissionHost] Accept loop error: {ex.Message}");
                _logService.Write(RenderLogLevel.Warning, RenderLogScope.Submission, $"submission host 接收请求失败: {ex.Message}", source: nameof(LocalSubmissionHost));
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Thread.Sleep(250);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(TimeSpan.FromSeconds(5));
        using var _ = client;
        client.ReceiveTimeout = 2000;
        client.SendTimeout = 2000;
        await using var stream = client.GetStream();

        try
        {
            var requestLine = await ReadRequestLineAsync(stream, requestCts.Token);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                await WriteResponseAsync(stream, new SubmissionWireResponse
                {
                    Ok = false,
                    Message = "Empty submission request.",
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                }, requestCts.Token);
                return;
            }

            var request = JsonSerializer.Deserialize(requestLine, SubmissionJsonContext.Default.SubmissionWireRequest);
            if (request == null)
            {
                await WriteResponseAsync(stream, new SubmissionWireResponse
                {
                    Ok = false,
                    Message = "Invalid submission payload.",
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                }, requestCts.Token);
                return;
            }

            if (!string.Equals(request.Token, CurrentEndpoint?.Token, StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, new SubmissionWireResponse
                {
                    RequestId = request.RequestId,
                    Ok = false,
                    Message = "Invalid submission token.",
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                }, requestCts.Token);
                return;
            }

            var response = await HandleRequestAsync(request, requestCts.Token);
            await WriteResponseAsync(stream, response, requestCts.Token);
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
            _logService.Write(RenderLogLevel.Warning, RenderLogScope.Submission, "submission 请求处理超时或已取消。", source: nameof(LocalSubmissionHost));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalSubmissionHost] Client handling error: {ex.Message}");
            _logService.Write(RenderLogLevel.Error, RenderLogScope.Submission, $"submission 请求处理失败: {ex.Message}", source: nameof(LocalSubmissionHost));

            try
            {
                await WriteResponseAsync(stream, new SubmissionWireResponse
                {
                    Ok = false,
                    Message = ex.Message,
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                }, CancellationToken.None);
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task<SubmissionWireResponse> HandleRequestAsync(SubmissionWireRequest request, CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case "ping":
                _logService.Write(RenderLogLevel.Debug, RenderLogScope.Submission, "收到 submission ping。", source: nameof(LocalSubmissionHost));
                return new SubmissionWireResponse
                {
                    RequestId = request.RequestId,
                    Ok = true,
                    Message = "pong",
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                };
            case "start_queue":
                _logService.Write(RenderLogLevel.Info, RenderLogScope.Submission, "收到 start_queue 请求。", source: nameof(LocalSubmissionHost));
                var startResponse = await _queueApplicationService.StartQueueFromSubmissionAsync(cancellationToken);
                return new SubmissionWireResponse
                {
                    RequestId = request.RequestId,
                    Ok = startResponse.Ok,
                    TaskId = startResponse.TaskId,
                    Message = startResponse.Message,
                    QueueState = startResponse.QueueState
                };
            case "submit_task":
                if (request.Payload == null)
                {
                    return new SubmissionWireResponse
                    {
                        RequestId = request.RequestId,
                        Ok = false,
                        Message = "submit_task requires a payload.",
                        QueueState = _queueApplicationService.Snapshot.State.ToString()
                    };
                }

                var submissionRequest = request.Payload.Value.Deserialize(SubmissionJsonContext.Default.LocalSubmissionRequest);
                if (submissionRequest == null)
                {
                    return new SubmissionWireResponse
                    {
                        RequestId = request.RequestId,
                        Ok = false,
                        Message = "submit_task payload is invalid.",
                        QueueState = _queueApplicationService.Snapshot.State.ToString()
                    };
                }

                var submissionResponse =
                    await _queueApplicationService.SubmitTaskAsync(submissionRequest, cancellationToken);
                _logService.Write(
                    submissionResponse.Ok ? RenderLogLevel.Info : RenderLogLevel.Error,
                    RenderLogScope.Submission,
                    submissionResponse.Ok
                        ? $"收到任务提交: {submissionRequest.Filepath}"
                        : $"任务提交失败: {submissionResponse.Message}",
                    source: nameof(LocalSubmissionHost));
                return new SubmissionWireResponse
                {
                    RequestId = request.RequestId,
                    Ok = submissionResponse.Ok,
                    TaskId = submissionResponse.TaskId,
                    Message = submissionResponse.Message,
                    QueueState = submissionResponse.QueueState
                };
            default:
                return new SubmissionWireResponse
                {
                    RequestId = request.RequestId,
                    Ok = false,
                    Message = $"Unknown submission command: {request.Command}",
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                };
        }
    }

    private async Task ProbeReadyAsync(CancellationToken cancellationToken)
    {
        if (CurrentEndpoint == null)
        {
            throw new InvalidOperationException("Submission endpoint metadata was not initialized.");
        }

        Exception? lastError = null;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var client = new TcpClient();
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeCts.CancelAfter(TimeSpan.FromMilliseconds(500));

                await client.ConnectAsync(CurrentEndpoint.Host, CurrentEndpoint.Port, probeCts.Token);
                await using var stream = client.GetStream();

                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new SubmissionWireRequest
                {
                    RequestId = "startup-probe",
                    Command = "ping",
                    Token = CurrentEndpoint.Token,
                    Payload = null
                }, SubmissionJsonContext.Default.SubmissionWireRequest) + "\n");

                await stream.WriteAsync(payload, probeCts.Token);
                await stream.FlushAsync(probeCts.Token);

                var responseLine = await ReadRequestLineAsync(stream, probeCts.Token);
                var response = JsonSerializer.Deserialize(responseLine, SubmissionJsonContext.Default.SubmissionWireResponse);
                if (response is { Ok: true } && string.Equals(response.Message, "pong", StringComparison.Ordinal))
                {
                    return;
                }

                lastError = new InvalidOperationException($"Unexpected startup probe response: {responseLine}");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException($"Local submission host failed its startup probe: {lastError?.Message}");
    }

    private async Task WriteEndpointFileAsync(SubmissionEndpointInfo endpointInfo, CancellationToken cancellationToken)
    {
        var endpointFilePath = SubmissionPaths.GetEndpointFilePath();
        await File.WriteAllTextAsync(
            endpointFilePath,
            JsonSerializer.Serialize(endpointInfo, SubmissionJsonContext.Default.SubmissionEndpointInfo),
            cancellationToken);
    }

    private static void DeleteEndpointFile()
    {
        try
        {
            var endpointFilePath = SubmissionPaths.GetEndpointFilePath();
            if (File.Exists(endpointFilePath))
            {
                File.Delete(endpointFilePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LocalSubmissionHost] Failed to delete endpoint file: {ex.Message}");
        }
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            var newlineIndex = Array.IndexOf(chunk, (byte)'\n', 0, bytesRead);
            if (newlineIndex >= 0)
            {
                buffer.Write(chunk, 0, newlineIndex);
                break;
            }

            buffer.Write(chunk, 0, bytesRead);

            if (buffer.Length > 64 * 1024)
            {
                throw new InvalidOperationException("Submission response exceeded the 64 KB limit.");
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).Trim();
    }

    private async Task WriteResponseAsync(NetworkStream stream, SubmissionWireResponse response, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, SubmissionJsonContext.Default.SubmissionWireResponse) + "\n");
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private void TrackClientTask(Task clientTask)
    {
        lock (_clientTasksLock)
        {
            _clientTasks.Add(clientTask);
        }

        _ = clientTask.ContinueWith(task =>
        {
            lock (_clientTasksLock)
            {
                _clientTasks.Remove(task);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task WaitForClientTasksAsync(CancellationToken cancellationToken)
    {
        Task[] tasks;
        lock (_clientTasksLock)
        {
            tasks = _clientTasks.ToArray();
        }

        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
        }
        catch
        {
            // ignored
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalSubmissionHost));
        }
    }
}
