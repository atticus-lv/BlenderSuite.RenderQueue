using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Services.Business.Blender.Extensions;

namespace BlenderRenderQueue.Services.Business.Blender.WorkerHost;

public sealed class PythonConsoleWorkerHost : IBlenderWorkerHost
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
            new Dictionary<string, object?>(),
            RequestTimeout,
            cancellationToken);

        State.LastHeartbeatAt = DateTimeOffset.UtcNow;
        State.ConsecutiveHeartbeatFailures = 0;

        return response;
    }

    public Task<BlenderWorkerResponse> QueryFileInfoAsync(CancellationToken cancellationToken = default)
    {
        return SendRequestAsync("query_file_info", new Dictionary<string, object?>(), RequestTimeout, cancellationToken);
    }

    public async Task<BlenderWorkerResponse> LoadFileAsync(string blendFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blendFilePath))
        {
            throw new ArgumentException("Blend file path is required.", nameof(blendFilePath));
        }

        var response = await SendRequestAsync(
            "load_file",
            new Dictionary<string, object?> { ["filepath"] = blendFilePath },
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
            var payload = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(request.SceneName))
            {
                payload["scene_name"] = request.SceneName;
            }

            if (request.SingleFrame.HasValue)
            {
                payload["single_frame"] = request.SingleFrame.Value;
            }

            if (request.FrameStart.HasValue)
            {
                payload["frame_start"] = request.FrameStart.Value;
            }

            if (request.FrameEnd.HasValue)
            {
                payload["frame_end"] = request.FrameEnd.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                payload["output_path"] = request.OutputPath;
            }

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
                await SendRequestAsync("shutdown", new Dictionary<string, object?>(), RequestTimeout, cancellationToken);
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
        await TerminateProcessCoreAsync();
        await CleanupStaleWorkerProcessAsync(blenderExecutablePath);

        _connectionInfo = BlenderWorkerConnectionInfo.CreateLocal();
        _appInstanceId = Guid.NewGuid().ToString("N");
        State.Status = "starting";
        State.IsProcessRunning = false;
        State.IsRendering = false;
        State.LastError = string.Empty;
        State.RenderStartedAt = null;
        State.CurrentFile = string.Empty;
        State.ActiveScene = string.Empty;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = blenderExecutablePath,
                Arguments = "--background --log-level info --python-console",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false)
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.Environment["BRQ_WORKER"] = "1";
        process.StartInfo.Environment["BRQ_APP_INSTANCE_ID"] = _appInstanceId;

        process.Exited += (_, _) =>
        {
            State.IsProcessRunning = false;
            var exitCode = process.HasExited ? process.ExitCode : -1;
            DeleteWorkerProcessInfo();
            OnProcessExited?.Invoke(exitCode);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Blender python-console worker process.");
        }

        _process = process;
        State.IsProcessRunning = true;
        PersistWorkerProcessInfo(process.Id, blenderExecutablePath);
        _stdoutTask = Task.Run(() => ReadOutputLoopAsync(process.StandardOutput, false, _disposeCts.Token));
        _stderrTask = Task.Run(() => ReadOutputLoopAsync(process.StandardError, true, _disposeCts.Token));

        await ProbeConsoleReadyAsync(cancellationToken);
        await InjectBootstrapScriptAsync(cancellationToken);
        await WaitForWorkerReadyAsync(cancellationToken);

        StartHeartbeatLoop();
    }

    private async Task ProbeConsoleReadyAsync(CancellationToken cancellationToken)
    {
        var sentinel = $"__BRQ_CONSOLE_READY__{Guid.NewGuid():N}";
        await SendConsoleCommandAsync($"print('{sentinel}')", cancellationToken);
        await WaitForOutputAsync(line => line.Contains(sentinel, StringComparison.Ordinal), ConsoleReadyTimeout, cancellationToken);
    }

    private async Task InjectBootstrapScriptAsync(CancellationToken cancellationToken)
    {
        var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Python", "python_console_worker.py");
        if (!File.Exists(bootstrapPath))
        {
            throw new FileNotFoundException("Python console worker bootstrap script was not found.", bootstrapPath);
        }

        var bootstrapText = await File.ReadAllTextAsync(bootstrapPath, cancellationToken);
        var bootstrapBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bootstrapText));
        var configJson = JsonSerializer.Serialize(new
        {
            host = _connectionInfo!.Host,
            port = _connectionInfo.Port,
            token = _connectionInfo.Token,
            app_instance_id = _appInstanceId,
            log_path = GetWorkerLogPath()
        });

        await SendConsoleCommandAsync("import base64, json", cancellationToken);
        await SendConsoleCommandAsync($"__brq_script = base64.b64decode('{bootstrapBase64}').decode('utf-8')", cancellationToken);
        await SendConsoleCommandAsync("exec(compile(__brq_script, '<brq_console_worker>', 'exec'), globals(), globals())", cancellationToken);
        await SendConsoleCommandAsync($"run_brq_worker_forever(json.loads(r'''{configJson}'''))", cancellationToken);
    }

    private async Task WaitForWorkerReadyAsync(CancellationToken cancellationToken)
    {
        await WaitForOutputAsync(
            line => line.Contains("__BRQ_WORKER_READY__", StringComparison.Ordinal),
            WorkerReadyTimeout,
            cancellationToken);

        var pingDeadline = DateTimeOffset.UtcNow + WorkerReadyTimeout;
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < pingDeadline)
        {
            try
            {
                var response = await SendRequestAsync(
                    "ping",
                    new Dictionary<string, object?>(),
                    RequestTimeout,
                    cancellationToken);

                if (response.Ok)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException($"Worker did not reach the ready state. Last error: {lastError?.Message}");
    }

    private async Task<BlenderWorkerResponse> SendRequestAsync(
        string command,
        IReadOnlyDictionary<string, object?> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_process is null || _process.HasExited || _connectionInfo is null)
        {
            throw new InvalidOperationException("The Blender worker process is not running.");
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            linkedCts.CancelAfter(timeout);

            using var client = new TcpClient();
            await client.ConnectAsync(_connectionInfo.Host, _connectionInfo.Port, linkedCts.Token);
            SetActiveRequestClient(client);

            await using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            var request = new
            {
                request_id = $"brq-{Interlocked.Increment(ref _requestSequence):D8}",
                command,
                token = _connectionInfo.Token,
                payload
            };

            var json = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(json);

            var responseLine = await reader.ReadLineAsync(linkedCts.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                throw new InvalidOperationException($"Worker returned an empty response for command '{command}'.");
            }

            var response = ParseResponse(responseLine);
            ApplyResponseState(response);

            if (!response.Ok)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Error)
                    ? $"Worker command '{command}' failed."
                    : response.Error);
            }

            return response;
        }
        finally
        {
            ClearActiveRequestClient();
        }
    }

    private BlenderWorkerResponse ParseResponse(string responseLine)
    {
        using var document = JsonDocument.Parse(responseLine);
        var root = document.RootElement;
        var payload = root.TryGetProperty("payload", out var payloadElement)
            ? payloadElement
            : default;

        return new BlenderWorkerResponse
        {
            RequestId = root.TryGetProperty("request_id", out var requestIdElement)
                ? requestIdElement.GetString() ?? string.Empty
                : string.Empty,
            Ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean(),
            WorkerState = root.TryGetProperty("worker_state", out var workerStateElement)
                ? workerStateElement.GetString() ?? string.Empty
                : string.Empty,
            Error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString() ?? string.Empty
                : string.Empty,
            CurrentFile = payload.TryGetProperty("current_file", out var currentFileElement)
                ? currentFileElement.GetString() ?? string.Empty
                : string.Empty,
            ActiveScene = payload.TryGetProperty("active_scene", out var activeSceneElement)
                ? activeSceneElement.GetString() ?? string.Empty
                : string.Empty,
            Scenes = payload.TryGetProperty("scenes", out var scenesElement) && scenesElement.ValueKind == JsonValueKind.Array
                ? scenesElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray()
                : [],
            Camera = payload.TryGetProperty("camera", out var cameraElement)
                ? cameraElement.GetString() ?? string.Empty
                : string.Empty,
            FrameStart = payload.TryGetProperty("frame_start", out var frameStartElement) && frameStartElement.TryGetInt32(out var frameStart)
                ? frameStart
                : 0,
            FrameEnd = payload.TryGetProperty("frame_end", out var frameEndElement) && frameEndElement.TryGetInt32(out var frameEnd)
                ? frameEnd
                : 0,
            OutputPath = payload.TryGetProperty("output_path", out var outputPathElement)
                ? outputPathElement.GetString() ?? string.Empty
                : string.Empty,
            IsSaved = payload.TryGetProperty("is_saved", out var isSavedElement) && isSavedElement.GetBoolean(),
            OutputVerified = payload.TryGetProperty("output_verified", out var outputVerifiedElement) && outputVerifiedElement.GetBoolean(),
            RenderStartedAt = payload.TryGetProperty("render_started_at", out var renderStartedAtElement)
                ? renderStartedAtElement.GetString() ?? string.Empty
                : string.Empty,
            LastHeartbeatAt = payload.TryGetProperty("last_heartbeat_at", out var heartbeatElement)
                ? heartbeatElement.GetString() ?? string.Empty
                : string.Empty
        };
    }

    private void ApplyResponseState(BlenderWorkerResponse response)
    {
        State.Status = response.WorkerState;
        State.CurrentFile = response.CurrentFile;
        State.ActiveScene = response.ActiveScene;
        State.LastError = response.Error;
        State.LastHeartbeatAt = ParseDateTime(response.LastHeartbeatAt);
        State.RenderStartedAt = ParseDateTime(response.RenderStartedAt);
        State.IsRendering = string.Equals(response.WorkerState, "rendering", StringComparison.Ordinal);
        State.IsProcessRunning = _process is { HasExited: false };

        if (!string.IsNullOrWhiteSpace(response.CurrentFile))
        {
            _lastLoadedBlendFilePath = response.CurrentFile;
        }
    }

    private async Task ReadOutputLoopAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (isError)
                {
                    HandleErrorLine(line);
                }
                else
                {
                    HandleOutputLine(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            HandleErrorLine($"Worker output loop failed: {ex.Message}");
        }
    }

    private void HandleOutputLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        State.LastOutputAt = DateTimeOffset.UtcNow;

        lock (_waitersLock)
        {
            foreach (var waiter in _outputWaiters.ToList())
            {
                if (!waiter.Predicate(line))
                {
                    continue;
                }

                waiter.CompletionSource.TrySetResult(line);
                _outputWaiters.Remove(waiter);
            }
        }

        OnOutputReceived?.Invoke(line);
    }

    private void HandleErrorLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        State.LastOutputAt = DateTimeOffset.UtcNow;
        State.LastError = line;
        OnErrorReceived?.Invoke(line);
    }

    private async Task<string> WaitForOutputAsync(
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var waiter = new OutputWaiter(predicate);
        lock (_waitersLock)
        {
            _outputWaiters.Add(waiter);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        linkedCts.CancelAfter(timeout);
        using var registration = linkedCts.Token.Register(() =>
        {
            waiter.CompletionSource.TrySetCanceled(linkedCts.Token);
            lock (_waitersLock)
            {
                _outputWaiters.Remove(waiter);
            }
        });

        return await waiter.CompletionSource.Task.WaitAsync(linkedCts.Token);
    }

    private async Task SendConsoleCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("The Blender console worker is not running.");
        }

        await _stdinLock.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _stdinLock.Release();
        }
    }

    private void StartHeartbeatLoop()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);

        _ = Task.Run(async () =>
        {
            while (!_heartbeatCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HeartbeatInterval, _heartbeatCts.Token);

                    if (_heartbeatCts.IsCancellationRequested || _disposed)
                    {
                        break;
                    }

                    if (_process is null || _process.HasExited)
                    {
                        State.ConsecutiveHeartbeatFailures++;
                        if (State.ConsecutiveHeartbeatFailures >= HeartbeatFailureThreshold)
                        {
                            await RecoverAsync(_heartbeatCts.Token);
                            State.ConsecutiveHeartbeatFailures = 0;
                        }

                        continue;
                    }

                    if (State.IsRendering)
                    {
                        continue;
                    }

                    try
                    {
                        await PingAsync(_heartbeatCts.Token);
                    }
                    catch
                    {
                        State.ConsecutiveHeartbeatFailures++;
                        if (State.ConsecutiveHeartbeatFailures >= HeartbeatFailureThreshold)
                        {
                            await RecoverAsync(_heartbeatCts.Token);
                            State.ConsecutiveHeartbeatFailures = 0;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _heartbeatCts.Token);
    }

    private async Task TerminateProcessCoreAsync()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
        AbortActiveRequestClient();

        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync(_disposeCts.Token);
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            _process.Dispose();
            _process = null;
            State.IsProcessRunning = false;
            DeleteWorkerProcessInfo();
        }
    }

    private async Task CleanupStaleWorkerProcessAsync(string blenderExecutablePath)
    {
        var metadataPath = GetWorkerProcessInfoPath();
        if (!File.Exists(metadataPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            var metadata = JsonSerializer.Deserialize<WorkerProcessMetadata>(json);
            if (metadata is null || metadata.ProcessId <= 0)
            {
                DeleteWorkerProcessInfo();
                return;
            }

            try
            {
                var process = Process.GetProcessById(metadata.ProcessId);
                if (process.HasExited)
                {
                    DeleteWorkerProcessInfo();
                    return;
                }

                if (!IsLikelyBlenderProcess(process, blenderExecutablePath))
                {
                    DeleteWorkerProcessInfo();
                    return;
                }

                process.Kill(true);
                await process.WaitForExitAsync(_disposeCts.Token);
            }
            catch (ArgumentException)
            {
                // The process no longer exists.
            }
            catch (InvalidOperationException)
            {
                // The process has already exited.
            }
        }
        catch
        {
            // If the metadata is unreadable, remove it and continue.
        }
        finally
        {
            DeleteWorkerProcessInfo();
        }
    }

    private string GetWorkerLogPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlenderRenderQueue",
            "Logs");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"worker-{_appInstanceId}.log");
    }

    private string GetWorkerProcessInfoPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlenderRenderQueue");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "python-console-worker.json");
    }

    private void PersistWorkerProcessInfo(int processId, string blenderExecutablePath)
    {
        var metadata = new WorkerProcessMetadata
        {
            ProcessId = processId,
            BlenderExecutablePath = blenderExecutablePath,
            AppInstanceId = _appInstanceId
        };

        File.WriteAllText(GetWorkerProcessInfoPath(), JsonSerializer.Serialize(metadata));
    }

    private void DeleteWorkerProcessInfo()
    {
        try
        {
            var path = GetWorkerProcessInfoPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool IsLikelyBlenderProcess(Process process, string blenderExecutablePath)
    {
        try
        {
            if (!process.ProcessName.Contains("blender", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var mainModulePath = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(mainModulePath))
                {
                    return string.Equals(
                        Path.GetFullPath(mainModulePath),
                        Path.GetFullPath(blenderExecutablePath),
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Accessing MainModule can fail on some platforms. Fall back to process name only.
            }

            return true;
        }
        catch
        {
            return false;
        }
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

    private sealed class WorkerProcessMetadata
    {
        public int ProcessId { get; init; }
        public string BlenderExecutablePath { get; init; } = string.Empty;
        public string AppInstanceId { get; init; } = string.Empty;
    }
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
