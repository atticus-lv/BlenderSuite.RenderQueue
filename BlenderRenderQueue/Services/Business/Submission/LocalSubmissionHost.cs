using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Application.Queue;

namespace BlenderRenderQueue.Services.Business.Submission;

public sealed class LocalSubmissionHost : ILocalSubmissionHost
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly IRenderQueueApplicationService _queueApplicationService;
    private readonly JsonSerializerOptions _wireJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly JsonSerializerOptions _endpointJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    private CancellationTokenSource? _hostCts;
    private TcpListener? _listener;
    private Thread? _acceptLoopThread;
    private bool _disposed;

    public LocalSubmissionHost(IRenderQueueApplicationService queueApplicationService)
    {
        _queueApplicationService = queueApplicationService;
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
            _hostCts?.Dispose();
            _hostCts = null;
            CurrentEndpoint = null;

            DeleteEndpointFile();
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
                HandleClient(client, cancellationToken);
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
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                Thread.Sleep(250);
            }
        }
    }

    private void HandleClient(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.ReceiveTimeout = 2000;
            client.SendTimeout = 2000;
            using var stream = client.GetStream();

            try
            {
                var requestLine = ReadRequestLine(stream, cancellationToken);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    WriteResponse(stream, new SubmissionWireResponse
                    {
                        Ok = false,
                        Message = "Empty submission request.",
                        QueueState = _queueApplicationService.Snapshot.State.ToString()
                    });
                    return;
                }

                var request = JsonSerializer.Deserialize<SubmissionWireRequest>(requestLine, _wireJsonOptions);
                if (request == null)
                {
                    WriteResponse(stream, new SubmissionWireResponse
                    {
                        Ok = false,
                        Message = "Invalid submission payload.",
                        QueueState = _queueApplicationService.Snapshot.State.ToString()
                    });
                    return;
                }

                if (!string.Equals(request.Token, CurrentEndpoint?.Token, StringComparison.Ordinal))
                {
                    WriteResponse(stream, new SubmissionWireResponse
                    {
                        RequestId = request.RequestId,
                        Ok = false,
                        Message = "Invalid submission token.",
                        QueueState = _queueApplicationService.Snapshot.State.ToString()
                    });
                    return;
                }

                var response = HandleRequestAsync(request, cancellationToken).GetAwaiter().GetResult();
                WriteResponse(stream, response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalSubmissionHost] Client handling error: {ex.Message}");
                WriteResponse(stream, new SubmissionWireResponse
                {
                    Ok = false,
                    Message = ex.Message,
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                });
            }
        }
    }

    private static string ReadRequestLine(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = stream.Read(chunk, 0, chunk.Length);
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
                throw new InvalidOperationException("Submission request exceeded the 64 KB limit.");
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).Trim();
    }

    private async Task<SubmissionWireResponse> HandleRequestAsync(SubmissionWireRequest request, CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case "ping":
                return new SubmissionWireResponse
                {
                    RequestId = request.RequestId,
                    Ok = true,
                    Message = "pong",
                    QueueState = _queueApplicationService.Snapshot.State.ToString()
                };
            case "start_queue":
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

                var submissionRequest = request.Payload.Value.Deserialize<LocalSubmissionRequest>(_wireJsonOptions);
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
                }, _wireJsonOptions) + "\n");

                await stream.WriteAsync(payload, probeCts.Token);
                await stream.FlushAsync(probeCts.Token);

                var responseLine = await ReadRequestLineAsync(stream, probeCts.Token);
                var response = JsonSerializer.Deserialize<SubmissionWireResponse>(responseLine, _wireJsonOptions);
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
            JsonSerializer.Serialize(endpointInfo, _endpointJsonOptions),
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

    private void WriteResponse(NetworkStream stream, SubmissionWireResponse response)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, _wireJsonOptions) + "\n");
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalSubmissionHost));
        }
    }

    private sealed class SubmissionWireRequest
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

    private sealed class SubmissionWireResponse
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
}
