using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.Blender.Extensions;

namespace BlenderRenderQueue.Services.Business.Blender.WorkerHost;

public sealed partial class PythonConsoleWorkerHost : IBlenderWorkerHost
{
    private static readonly TimeSpan ConsoleReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WorkerReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RenderRequestTimeout = TimeSpan.FromHours(6);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private const int HeartbeatFailureThreshold = 3;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _stdinLock = new(1, 1);
    private readonly Lock _activeRequestLock = new();
    private readonly Lock _recentOutputLock = new();
    private readonly object _waitersLock = new();
    private readonly List<OutputWaiter> _outputWaiters = [];
    private readonly CancellationTokenSource _disposeCts = new();

    private Process? _process;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private CancellationTokenSource? _heartbeatCts;
    private BlenderWorkerConnectionInfo? _connectionInfo;
    private string _appInstanceId = string.Empty;
    private string _lastLoadedBlendFilePath = string.Empty;
    private int _requestSequence;
    private bool _disposed;
    private TcpClient? _activeRequestClient;
    private Process? _terminatingProcess;
    private long _processGeneration;
    private readonly Queue<string> _recentOutputLines = new();
    private const int MaxRecentOutputLines = 120;
    private bool _sawBlenderQuitLine;
    private DateTimeOffset _processStartedAtUtc;

    public BlenderWorkerHostState State { get; } = new();

    public event Action<string>? OnOutputReceived;
    public event Action<string>? OnErrorReceived;
    public event Action<int>? OnProcessExited;

    public async Task EnsureReadyAsync(string blenderExecutablePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blenderExecutablePath) || !File.Exists(blenderExecutablePath))
        {
            throw new FileNotFoundException("A valid Blender executable path is required.", blenderExecutablePath);
        }

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();

            if (_process is { HasExited: false } &&
                string.Equals(State.BlenderExecutablePath, blenderExecutablePath, StringComparison.Ordinal))
            {
                if (State.Status is "ready" or "rendering" or "loading")
                {
                    return;
                }
            }

            State.BlenderExecutablePath = blenderExecutablePath;
            await StartWorkerProcessCoreAsync(blenderExecutablePath, cancellationToken);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<BlenderWorkerResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(
            "ping",
            WorkerRequestPayload.Empty,
            RequestTimeout,
            cancellationToken);

        State.LastHeartbeatAt = DateTimeOffset.UtcNow;
        State.ConsecutiveHeartbeatFailures = 0;

        return response;
    }

    public Task<BlenderWorkerResponse> QueryFileInfoAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync("query_file_info", WorkerRequestPayload.Empty, RequestTimeout, cancellationToken);
    }

    public async Task<BlenderWorkerResponse> LoadFileAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blendFilePath))
        {
            throw new ArgumentException("Blend file path is required.", nameof(blendFilePath));
        }

        var response = await SendRequestAsync(
            "load_file",
            new WorkerRequestPayload
            {
                Filepath = blendFilePath
            },
            RequestTimeout,
            cancellationToken);

        if (response.Ok)
        {
            _lastLoadedBlendFilePath = blendFilePath;
        }

        return response;
    }

    public async Task<BlenderWorkerResponse> RenderTaskAsync(BlenderWorkerRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BlendFilePath))
        {
            throw new ArgumentException("Blend file path is required.", nameof(request));
        }

        if (!string.Equals(State.CurrentFile, request.BlendFilePath, StringComparison.Ordinal))
        {
            await LoadFileAsync(request.BlendFilePath, cancellationToken);
        }

        State.IsRendering = true;
        State.Status = "rendering";
        State.RenderStartedAt = DateTimeOffset.UtcNow;

        try
        {
            var payload = new WorkerRequestPayload
            {
                SceneName = string.IsNullOrWhiteSpace(request.SceneName) ? null : request.SceneName,
                SingleFrame = request.SingleFrame,
                FrameStart = request.FrameStart,
                FrameEnd = request.FrameEnd,
                OutputPath = string.IsNullOrWhiteSpace(request.OutputPath) ? null : request.OutputPath
            };

            var response = await SendRequestAsync("render_task", payload, RenderRequestTimeout, cancellationToken);
            response = response.WithOutputVerified(VerifyRenderOutput(request, response));

            if (!response.OutputVerified)
            {
                throw new InvalidOperationException("Worker reported success but no render output was verified on disk.");
            }

            return response;
        }
        finally
        {
            State.IsRendering = false;
            if (State.Status == "rendering")
            {
                State.Status = "ready";
            }
        }
    }

    public async Task CancelCurrentRenderAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_process is null)
            {
                return;
            }

            State.Status = "cancelled";
            State.IsRendering = false;
            await TerminateProcessCoreAsync();

            if (!string.IsNullOrWhiteSpace(State.BlenderExecutablePath))
            {
                await StartWorkerProcessCoreAsync(State.BlenderExecutablePath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(_lastLoadedBlendFilePath))
                {
                    await LoadFileAsync(_lastLoadedBlendFilePath, cancellationToken);
                }
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<BlenderWorkerRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(State.BlenderExecutablePath))
            {
                return new BlenderWorkerRecoveryResult
                {
                    Recovered = false,
                    Message = "Worker recovery skipped because no Blender executable is configured."
                };
            }

            State.Status = "restarting";
            await TerminateProcessCoreAsync();
            await StartWorkerProcessCoreAsync(State.BlenderExecutablePath, cancellationToken);

            string reloadedFile = string.Empty;
            if (!string.IsNullOrWhiteSpace(_lastLoadedBlendFilePath))
            {
                await LoadFileAsync(_lastLoadedBlendFilePath, cancellationToken);
                reloadedFile = _lastLoadedBlendFilePath;
            }

            return new BlenderWorkerRecoveryResult
            {
                Recovered = true,
                ReloadedFile = reloadedFile,
                Message = string.IsNullOrWhiteSpace(reloadedFile)
                    ? "Worker restarted successfully."
                    : $"Worker restarted and reloaded {reloadedFile}."
            };
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
            if (_process is null)
            {
                return;
            }

            try
            {
                await SendRequestAsync("shutdown", WorkerRequestPayload.Empty, RequestTimeout, cancellationToken);
            }
            catch
            {
                // Fall back to a hard stop below.
            }

            await TerminateProcessCoreAsync();
            State.Status = "stopped";
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
        _disposeCts.Cancel();

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
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _stdinLock.Dispose();
            _lifecycleLock.Dispose();
            _disposeCts.Dispose();
        }
    }

    private async Task StartWorkerProcessCoreAsync(string blenderExecutablePath, CancellationToken cancellationToken)
    {
        await _processController.StartWorkerProcessCoreAsync(blenderExecutablePath, cancellationToken);
    }

    private async Task ProbeConsoleReadyAsync(CancellationToken cancellationToken)
    {
        await _transportClient.ProbeConsoleReadyAsync(cancellationToken);
    }

    private async Task InjectBootstrapScriptAsync(CancellationToken cancellationToken)
    {
        await _transportClient.InjectBootstrapScriptAsync(cancellationToken);
    }

    private async Task WaitForWorkerReadyAsync(CancellationToken cancellationToken)
    {
        await _transportClient.WaitForWorkerReadyAsync(cancellationToken);
    }

    private async Task<BlenderWorkerResponse> SendRequestAsync(
        string command,
        WorkerRequestPayload payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await _transportClient.SendRequestAsync(command, payload, timeout, cancellationToken);
    }

    private BlenderWorkerResponse ParseResponse(string responseLine)
    {
        return _transportClient.ParseResponse(responseLine);
    }

    private void ApplyResponseState(BlenderWorkerResponse response)
    {
        _transportClient.ApplyResponseState(response);
    }

    private async Task ReadOutputLoopAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
    {
        await _transportClient.ReadOutputLoopAsync(reader, isError, cancellationToken);
    }

    private void HandleOutputLine(string line)
    {
        _transportClient.HandleOutputLine(line);
    }

    private void HandleErrorLine(string line)
    {
        _transportClient.HandleErrorLine(line);
    }

    private static bool IsIgnorableConsoleNoise(string line)
    {
        return WorkerTransportClient.IsIgnorableConsoleNoise(line);
    }

    private async Task<string> WaitForOutputAsync(
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await _transportClient.WaitForOutputAsync(predicate, timeout, cancellationToken);
    }

    private async Task SendConsoleCommandAsync(string command, CancellationToken cancellationToken)
    {
        await _transportClient.SendConsoleCommandAsync(command, cancellationToken);
    }

    private void StartHeartbeatLoop()
    {
        _transportClient.StartHeartbeatLoop();
    }

    private async Task TerminateProcessCoreAsync()
    {
        await _processController.TerminateProcessCoreAsync();
    }

    private async Task CleanupStaleWorkerProcessAsync(string blenderExecutablePath)
    {
        await _processController.CleanupStaleWorkerProcessAsync(blenderExecutablePath);
    }

    private string GetWorkerLogPath()
    {
        return _processController.GetWorkerLogPath();
    }

    private string GetWorkerProcessInfoPath()
    {
        return _processController.GetWorkerProcessInfoPath();
    }

    private void PersistWorkerProcessInfo(int processId, string blenderExecutablePath)
    {
        _processController.PersistWorkerProcessInfo(processId, blenderExecutablePath);
    }

    private void DeleteWorkerProcessInfo()
    {
        _processController.DeleteWorkerProcessInfo();
    }

    private static bool IsLikelyBlenderProcess(Process process, string blenderExecutablePath)
    {
        return WorkerProcessController.IsLikelyBlenderProcess(process, blenderExecutablePath);
    }

    private bool VerifyRenderOutput(BlenderWorkerRequest request, BlenderWorkerResponse response)
    {
        if (request.SingleFrame.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                return File.Exists(request.OutputPath);
            }

            return !string.IsNullOrWhiteSpace(response.OutputPath) && File.Exists(response.OutputPath);
        }

        var outputPath = !string.IsNullOrWhiteSpace(request.OutputPath) ? request.OutputPath : response.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return true;
        }

        var directory = ResolveAnimationOutputDirectory(outputPath);
        return Directory.Exists(directory) && Directory.EnumerateFiles(directory).Any();
    }

    private static string ResolveAnimationOutputDirectory(string outputPath)
    {
        if (Directory.Exists(outputPath))
        {
            return outputPath;
        }

        var sanitized = outputPath.Replace("#", string.Empty);
        var directory = Path.GetDirectoryName(sanitized);
        return string.IsNullOrWhiteSpace(directory) ? outputPath : directory;
    }

    private static DateTimeOffset? ParseDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private void RecordRecentOutputLine(string line)
    {
        _diagnosticsService.RecordRecentOutputLine(line);
    }

    private void ClearRecentOutputLines()
    {
        _diagnosticsService.ClearRecentOutputLines();
    }

    private string BuildUnexpectedExitDiagnostic(int exitCode)
    {
        return _diagnosticsService.BuildUnexpectedExitDiagnostic(exitCode);
    }

    private string ClassifyProcessExit(int exitCode)
    {
        return _diagnosticsService.ClassifyProcessExit(exitCode);
    }

    private static string ClassifyErrorText(string? text)
    {
        return WorkerDiagnosticsService.ClassifyErrorText(text);
    }

    private string GetRecentOutputTail(int maxLines)
    {
        return _diagnosticsService.GetRecentOutputTail(maxLines);
    }

    private string FindCrashReportPath()
    {
        return _diagnosticsService.FindCrashReportPath();
    }

    private static string FindNewestCrashFile(string directory, IReadOnlyList<string> patterns, DateTimeOffset processStartedAtUtc)
    {
        return WorkerDiagnosticsService.FindNewestCrashFile(directory, patterns, processStartedAtUtc);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PythonConsoleWorkerHost));
        }
    }

    private void SetActiveRequestClient(TcpClient client)
    {
        lock (_activeRequestLock)
        {
            _activeRequestClient = client;
        }
    }

    private void ClearActiveRequestClient()
    {
        lock (_activeRequestLock)
        {
            _activeRequestClient = null;
        }
    }

    private void AbortActiveRequestClient()
    {
        lock (_activeRequestLock)
        {
            try
            {
                _activeRequestClient?.Dispose();
            }
            catch
            {
                // ignored
            }
            finally
            {
                _activeRequestClient = null;
            }
        }
    }

    private sealed class OutputWaiter(Func<string, bool> predicate)
    {
        public Func<string, bool> Predicate { get; } = predicate;
        public TaskCompletionSource<string> CompletionSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkerBootstrapConfig))]
[JsonSerializable(typeof(WorkerRequestPayload))]
[JsonSerializable(typeof(WorkerWireRequest))]
[JsonSerializable(typeof(WorkerWireResponse))]
[JsonSerializable(typeof(WorkerResponsePayload))]
[JsonSerializable(typeof(WorkerProcessMetadata))]
internal partial class WorkerHostJsonContext : JsonSerializerContext
{
}

internal sealed class WorkerBootstrapConfig
{
    [JsonPropertyName("host")]
    public string Host { get; init; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; init; }

    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("app_instance_id")]
    public string AppInstanceId { get; init; } = string.Empty;

    [JsonPropertyName("log_path")]
    public string LogPath { get; init; } = string.Empty;
}

internal sealed class WorkerRequestPayload
{
    public static WorkerRequestPayload Empty { get; } = new();

    [JsonPropertyName("filepath")]
    public string? Filepath { get; init; }

    [JsonPropertyName("scene_name")]
    public string? SceneName { get; init; }

    [JsonPropertyName("single_frame")]
    public int? SingleFrame { get; init; }

    [JsonPropertyName("frame_start")]
    public int? FrameStart { get; init; }

    [JsonPropertyName("frame_end")]
    public int? FrameEnd { get; init; }

    [JsonPropertyName("output_path")]
    public string? OutputPath { get; init; }
}

internal sealed class WorkerWireRequest
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public WorkerRequestPayload Payload { get; init; } = WorkerRequestPayload.Empty;
}

internal sealed class WorkerWireResponse
{
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("worker_state")]
    public string? WorkerState { get; init; }

    [JsonPropertyName("payload")]
    public WorkerResponsePayload? Payload { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_category")]
    public string? ErrorCategory { get; init; }
}

internal sealed class WorkerResponsePayload
{
    [JsonPropertyName("current_file")]
    public string? CurrentFile { get; init; }

    [JsonPropertyName("active_scene")]
    public string? ActiveScene { get; init; }

    [JsonPropertyName("scenes")]
    public string[]? Scenes { get; init; }

    [JsonPropertyName("camera")]
    public string? Camera { get; init; }

    [JsonPropertyName("frame_start")]
    public int? FrameStart { get; init; }

    [JsonPropertyName("frame_end")]
    public int? FrameEnd { get; init; }

    [JsonPropertyName("output_path")]
    public string? OutputPath { get; init; }

    [JsonPropertyName("is_saved")]
    public bool? IsSaved { get; init; }

    [JsonPropertyName("output_verified")]
    public bool? OutputVerified { get; init; }

    [JsonPropertyName("render_started_at")]
    public string? RenderStartedAt { get; init; }

    [JsonPropertyName("last_heartbeat_at")]
    public string? LastHeartbeatAt { get; init; }
}

internal sealed class WorkerProcessMetadata
{
    public int ProcessId { get; init; }
    public string BlenderExecutablePath { get; init; } = string.Empty;
    public string AppInstanceId { get; init; } = string.Empty;
}

internal static class BlenderWorkerResponseExtensions
{
    public static BlenderWorkerResponse WithOutputVerified(this BlenderWorkerResponse response, bool outputVerified)
    {
        return new BlenderWorkerResponse
        {
            RequestId = response.RequestId,
            Ok = response.Ok,
            WorkerState = response.WorkerState,
            Error = response.Error,
            ErrorCategory = response.ErrorCategory,
            CurrentFile = response.CurrentFile,
            ActiveScene = response.ActiveScene,
            Scenes = response.Scenes,
            Camera = response.Camera,
            FrameStart = response.FrameStart,
            FrameEnd = response.FrameEnd,
            OutputPath = response.OutputPath,
            IsSaved = response.IsSaved,
            OutputVerified = outputVerified,
            RenderStartedAt = response.RenderStartedAt,
            LastHeartbeatAt = response.LastHeartbeatAt
        };
    }
}
