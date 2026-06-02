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
using System.Threading;
using System.Threading.Tasks;
using BlenderRenderQueue.Extensions;

namespace BlenderRenderQueue.Services.Business.Blender.WorkerHost;

public sealed partial class PythonConsoleWorkerHost
{
    private readonly WorkerProcessController _processController;
    private readonly WorkerTransportClient _transportClient;
    private readonly WorkerDiagnosticsService _diagnosticsService;

    public PythonConsoleWorkerHost()
    {
        _processController = new WorkerProcessController(this);
        _transportClient = new WorkerTransportClient(this);
        _diagnosticsService = new WorkerDiagnosticsService(this);
    }

    private sealed class WorkerProcessController(PythonConsoleWorkerHost owner)
    {
        private readonly PythonConsoleWorkerHost _owner = owner;

        public async Task StartWorkerProcessCoreAsync(string blenderExecutablePath, CancellationToken cancellationToken)
        {
            await TerminateProcessCoreAsync();
            await CleanupStaleWorkerProcessAsync(blenderExecutablePath);

            _owner._connectionInfo = BlenderWorkerConnectionInfo.CreateLocal();
            _owner._appInstanceId = Guid.NewGuid().ToString("N");
            _owner.State.ProcessGeneration = Interlocked.Increment(ref _owner._processGeneration);
            _owner.State.Status = "starting";
            _owner.State.IsProcessRunning = false;
            _owner.State.IsRendering = false;
            _owner.State.LastError = string.Empty;
            _owner.State.LastErrorCategory = string.Empty;
            _owner.State.RenderStartedAt = null;
            _owner.State.CurrentFile = string.Empty;
            _owner.State.ActiveScene = string.Empty;
            _owner._sawBlenderQuitLine = false;
            _owner._processStartedAtUtc = DateTimeOffset.UtcNow;
            _owner._diagnosticsService.ClearRecentOutputLines();

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
            process.StartInfo.Environment["BRQ_APP_INSTANCE_ID"] = _owner._appInstanceId;

            process.Exited += (_, _) =>
            {
                _owner.State.IsProcessRunning = false;
                var exitCode = process.HasExited ? process.ExitCode : -1;
                DeleteWorkerProcessInfo();
                if (!ReferenceEquals(process, _owner._process))
                {
                    return;
                }

                if (ReferenceEquals(process, _owner._terminatingProcess))
                {
                    return;
                }

                var diagnostic = _owner._diagnosticsService.BuildUnexpectedExitDiagnostic(exitCode);
                _owner.State.LastError = diagnostic;
                _owner.State.LastErrorCategory = _owner._diagnosticsService.ClassifyProcessExit(exitCode);
                _owner.OnErrorReceived?.Invoke(diagnostic);
                _owner.OnProcessExited?.Invoke(exitCode);
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start Blender python-console worker process.");
            }

            _owner._process = process;
            _owner.State.IsProcessRunning = true;
            PersistWorkerProcessInfo(process.Id, blenderExecutablePath);
            _owner._stdoutTask =
                Task.Run(() => _owner._transportClient.ReadOutputLoopAsync(process.StandardOutput, false, _owner._disposeCts.Token));
            _owner._stderrTask =
                Task.Run(() => _owner._transportClient.ReadOutputLoopAsync(process.StandardError, true, _owner._disposeCts.Token));
            _owner._stdoutTask.FireAndForget(
                source: nameof(PythonConsoleWorkerHost),
                message: "Blender worker 标准输出读取后台任务失败。");
            _owner._stderrTask.FireAndForget(
                source: nameof(PythonConsoleWorkerHost),
                message: "Blender worker 标准错误读取后台任务失败。");

            await _owner._transportClient.ProbeConsoleReadyAsync(cancellationToken);
            await _owner._transportClient.InjectBootstrapScriptAsync(cancellationToken);
            await _owner._transportClient.WaitForWorkerReadyAsync(cancellationToken);

            _owner._transportClient.StartHeartbeatLoop();
        }

        public async Task TerminateProcessCoreAsync()
        {
            _owner._heartbeatCts?.Cancel();
            _owner._heartbeatCts?.Dispose();
            _owner._heartbeatCts = null;
            _owner.AbortActiveRequestClient();

            if (_owner._process is null)
            {
                return;
            }

            var process = _owner._process;
            _owner._terminatingProcess = process;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(_owner._disposeCts.Token);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
                if (ReferenceEquals(_owner._process, process))
                {
                    _owner._process = null;
                }

                if (ReferenceEquals(_owner._terminatingProcess, process))
                {
                    _owner._terminatingProcess = null;
                }

                _owner.State.IsProcessRunning = false;
                DeleteWorkerProcessInfo();
            }
        }

        public async Task CleanupStaleWorkerProcessAsync(string blenderExecutablePath)
        {
            var metadataPath = GetWorkerProcessInfoPath();
            if (!File.Exists(metadataPath))
            {
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                var metadata = JsonSerializer.Deserialize(json, WorkerHostJsonContext.Default.WorkerProcessMetadata);
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
                    await process.WaitForExitAsync(_owner._disposeCts.Token);
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
            catch
            {
            }
            finally
            {
                DeleteWorkerProcessInfo();
            }
        }

        public string GetWorkerLogPath()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlenderRenderQueue",
                "Logs");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"worker-{_owner._appInstanceId}.log");
        }

        public string GetWorkerProcessInfoPath()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlenderRenderQueue");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "python-console-worker.json");
        }

        public void PersistWorkerProcessInfo(int processId, string blenderExecutablePath)
        {
            var metadata = new WorkerProcessMetadata
            {
                ProcessId = processId,
                BlenderExecutablePath = blenderExecutablePath,
                AppInstanceId = _owner._appInstanceId
            };

            File.WriteAllText(
                GetWorkerProcessInfoPath(),
                JsonSerializer.Serialize(metadata, WorkerHostJsonContext.Default.WorkerProcessMetadata));
        }

        public void DeleteWorkerProcessInfo()
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
            }
        }

        public static bool IsLikelyBlenderProcess(Process process, string blenderExecutablePath)
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
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class WorkerTransportClient(PythonConsoleWorkerHost owner)
    {
        private readonly PythonConsoleWorkerHost _owner = owner;

        public async Task ProbeConsoleReadyAsync(CancellationToken cancellationToken)
        {
            var sentinel = $"__BRQ_CONSOLE_READY__{Guid.NewGuid():N}";
            await SendConsoleCommandAsync($"print('{sentinel}')", cancellationToken);
            await WaitForOutputAsync(line => line.Contains(sentinel, StringComparison.Ordinal), ConsoleReadyTimeout,
                cancellationToken);
        }

        public async Task InjectBootstrapScriptAsync(CancellationToken cancellationToken)
        {
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Python", "python_console_worker.py");
            if (!File.Exists(bootstrapPath))
            {
                throw new FileNotFoundException("Python console worker bootstrap script was not found.", bootstrapPath);
            }

            var bootstrapText = await File.ReadAllTextAsync(bootstrapPath, cancellationToken);
            var bootstrapBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bootstrapText));
            var configJson = JsonSerializer.Serialize(
                new WorkerBootstrapConfig
                {
                    Host = _owner._connectionInfo!.Host,
                    Port = _owner._connectionInfo.Port,
                    Token = _owner._connectionInfo.Token,
                    AppInstanceId = _owner._appInstanceId,
                    LogPath = _owner._processController.GetWorkerLogPath()
                },
                WorkerHostJsonContext.Default.WorkerBootstrapConfig);

            await SendConsoleCommandAsync("import base64, json", cancellationToken);
            await SendConsoleCommandAsync($"__brq_script = base64.b64decode('{bootstrapBase64}').decode('utf-8')",
                cancellationToken);
            await SendConsoleCommandAsync("exec(compile(__brq_script, '<brq_console_worker>', 'exec'), globals(), globals())",
                cancellationToken);
            await SendConsoleCommandAsync($"run_brq_worker_forever(json.loads(r'''{configJson}'''))", cancellationToken);
        }

        public async Task WaitForWorkerReadyAsync(CancellationToken cancellationToken)
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
                        WorkerRequestPayload.Empty,
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

        public async Task<BlenderWorkerResponse> SendRequestAsync(
            string command,
            WorkerRequestPayload payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            _owner.ThrowIfDisposed();

            if (_owner._process is null || _owner._process.HasExited || _owner._connectionInfo is null)
            {
                throw new InvalidOperationException("The Blender worker process is not running.");
            }

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _owner._disposeCts.Token);
                linkedCts.CancelAfter(timeout);

                using var client = new TcpClient();
                await client.ConnectAsync(_owner._connectionInfo.Host, _owner._connectionInfo.Port, linkedCts.Token);
                _owner.SetActiveRequestClient(client);

                await using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

                var request = new WorkerWireRequest
                {
                    RequestId = $"brq-{Interlocked.Increment(ref _owner._requestSequence):D8}",
                    Command = command,
                    Token = _owner._connectionInfo.Token,
                    Payload = payload
                };

                var json = JsonSerializer.Serialize(request, WorkerHostJsonContext.Default.WorkerWireRequest);
                await writer.WriteLineAsync(json);

                var responseLine = await reader.ReadLineAsync(linkedCts.Token);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    if (_owner._process is null || _owner._process.HasExited)
                    {
                        throw new InvalidOperationException(
                            _owner._diagnosticsService.BuildUnexpectedExitDiagnostic(_owner._process?.HasExited == true
                                ? _owner._process.ExitCode
                                : -1));
                    }

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
                _owner.ClearActiveRequestClient();
            }
        }

        public BlenderWorkerResponse ParseResponse(string responseLine)
        {
            var response = JsonSerializer.Deserialize(responseLine, WorkerHostJsonContext.Default.WorkerWireResponse)
                           ?? throw new InvalidOperationException("Worker returned an unreadable JSON response.");
            var payload = response.Payload;

            return new BlenderWorkerResponse
            {
                RequestId = response.RequestId ?? string.Empty,
                Ok = response.Ok,
                WorkerState = response.WorkerState ?? string.Empty,
                Error = response.Error ?? string.Empty,
                ErrorCategory = response.ErrorCategory ?? string.Empty,
                CurrentFile = payload?.CurrentFile ?? string.Empty,
                ActiveScene = payload?.ActiveScene ?? string.Empty,
                Scenes = payload?.Scenes ?? [],
                Camera = payload?.Camera ?? string.Empty,
                FrameStart = payload?.FrameStart ?? 0,
                FrameEnd = payload?.FrameEnd ?? 0,
                OutputPath = payload?.OutputPath ?? string.Empty,
                IsSaved = payload?.IsSaved ?? false,
                OutputVerified = payload?.OutputVerified ?? false,
                RenderStartedAt = payload?.RenderStartedAt ?? string.Empty,
                LastHeartbeatAt = payload?.LastHeartbeatAt ?? string.Empty
            };
        }

        public void ApplyResponseState(BlenderWorkerResponse response)
        {
            _owner.State.Status = response.WorkerState;
            _owner.State.CurrentFile = response.CurrentFile;
            _owner.State.ActiveScene = response.ActiveScene;
            _owner.State.LastError = response.Error;
            _owner.State.LastErrorCategory = !string.IsNullOrWhiteSpace(response.ErrorCategory)
                ? response.ErrorCategory
                : WorkerDiagnosticsService.ClassifyErrorText(response.Error);
            _owner.State.LastHeartbeatAt = ParseDateTime(response.LastHeartbeatAt);
            _owner.State.RenderStartedAt = ParseDateTime(response.RenderStartedAt);
            _owner.State.IsRendering = string.Equals(response.WorkerState, "rendering", StringComparison.Ordinal);
            _owner.State.IsProcessRunning = _owner._process is { HasExited: false };

            if (!string.IsNullOrWhiteSpace(response.CurrentFile))
            {
                _owner._lastLoadedBlendFilePath = response.CurrentFile;
            }
        }

        public async Task ReadOutputLoopAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
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
            }
            catch (Exception ex)
            {
                HandleErrorLine($"Worker output loop failed: {ex.Message}");
            }
        }

        public void HandleOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            _owner.State.LastOutputAt = DateTimeOffset.UtcNow;
            _owner._diagnosticsService.RecordRecentOutputLine("[stdout] " + line);
            if (line.Contains("Blender quit", StringComparison.OrdinalIgnoreCase))
            {
                _owner._sawBlenderQuitLine = true;
            }

            lock (_owner._waitersLock)
            {
                foreach (var waiter in _owner._outputWaiters.ToList())
                {
                    if (!waiter.Predicate(line))
                    {
                        continue;
                    }

                    waiter.CompletionSource.TrySetResult(line);
                    _owner._outputWaiters.Remove(waiter);
                }
            }

            _owner.OnOutputReceived?.Invoke(line);
        }

        public void HandleErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (IsIgnorableConsoleNoise(line))
            {
                return;
            }

            _owner.State.LastOutputAt = DateTimeOffset.UtcNow;
            _owner._diagnosticsService.RecordRecentOutputLine("[stderr] " + line);
            if (line.Contains("Blender quit", StringComparison.OrdinalIgnoreCase))
            {
                _owner._sawBlenderQuitLine = true;
            }

            _owner.State.LastError = line;
            var category = WorkerDiagnosticsService.ClassifyErrorText(line);
            if (!string.IsNullOrWhiteSpace(category))
            {
                _owner.State.LastErrorCategory = category;
            }

            _owner.OnErrorReceived?.Invoke(line);
        }

        public static bool IsIgnorableConsoleNoise(string line)
        {
            var trimmed = line.Trim();
            return trimmed is "(InteractiveConsole)" or "now exiting InteractiveConsole..."
                || trimmed.StartsWith("Python ", StringComparison.Ordinal)
                || trimmed.StartsWith("Type \"help\"", StringComparison.Ordinal);
        }

        public async Task<string> WaitForOutputAsync(
            Func<string, bool> predicate,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var waiter = new OutputWaiter(predicate);
            lock (_owner._waitersLock)
            {
                _owner._outputWaiters.Add(waiter);
            }

            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _owner._disposeCts.Token);
            linkedCts.CancelAfter(timeout);
            using var registration = linkedCts.Token.Register(() =>
            {
                waiter.CompletionSource.TrySetCanceled(linkedCts.Token);
                lock (_owner._waitersLock)
                {
                    _owner._outputWaiters.Remove(waiter);
                }
            });

            return await waiter.CompletionSource.Task.WaitAsync(linkedCts.Token);
        }

        public async Task SendConsoleCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (_owner._process is null || _owner._process.HasExited)
            {
                throw new InvalidOperationException("The Blender console worker is not running.");
            }

            await _owner._stdinLock.WaitAsync(cancellationToken);
            try
            {
                await _owner._process.StandardInput.WriteLineAsync(command);
                await _owner._process.StandardInput.FlushAsync(cancellationToken);
            }
            finally
            {
                _owner._stdinLock.Release();
            }
        }

        public void StartHeartbeatLoop()
        {
            _owner._heartbeatCts?.Cancel();
            _owner._heartbeatCts?.Dispose();
            _owner._heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(_owner._disposeCts.Token);

            Task.Run(async () =>
            {
                while (!_owner._heartbeatCts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(HeartbeatInterval, _owner._heartbeatCts.Token);

                        if (_owner._heartbeatCts.IsCancellationRequested || _owner._disposed)
                        {
                            break;
                        }

                        if (_owner.State.IsRendering)
                        {
                            continue;
                        }

                        if (_owner._process is null || _owner._process.HasExited)
                        {
                            _owner.State.ConsecutiveHeartbeatFailures++;
                            if (_owner.State.ConsecutiveHeartbeatFailures >= HeartbeatFailureThreshold)
                            {
                                await _owner.RecoverAsync(_owner._heartbeatCts.Token);
                                _owner.State.ConsecutiveHeartbeatFailures = 0;
                            }

                            continue;
                        }

                        try
                        {
                            await _owner.PingAsync(_owner._heartbeatCts.Token);
                        }
                        catch
                        {
                            _owner.State.ConsecutiveHeartbeatFailures++;
                            if (_owner.State.ConsecutiveHeartbeatFailures >= HeartbeatFailureThreshold)
                            {
                                await _owner.RecoverAsync(_owner._heartbeatCts.Token);
                                _owner.State.ConsecutiveHeartbeatFailures = 0;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, _owner._heartbeatCts.Token).FireAndForget(
                source: nameof(PythonConsoleWorkerHost),
                message: "Blender worker 心跳后台任务失败。");
        }

        private static DateTimeOffset? ParseDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : null;
        }
    }

    private sealed class WorkerDiagnosticsService(PythonConsoleWorkerHost owner)
    {
        private readonly PythonConsoleWorkerHost _owner = owner;

        public void RecordRecentOutputLine(string line)
        {
            lock (_owner._recentOutputLock)
            {
                _owner._recentOutputLines.Enqueue($"{DateTimeOffset.UtcNow:O} {line}");
                while (_owner._recentOutputLines.Count > MaxRecentOutputLines)
                {
                    _owner._recentOutputLines.Dequeue();
                }
            }
        }

        public void ClearRecentOutputLines()
        {
            lock (_owner._recentOutputLock)
            {
                _owner._recentOutputLines.Clear();
            }
        }

        public string BuildUnexpectedExitDiagnostic(int exitCode)
        {
            var parts = new List<string>();
            var category = ClassifyProcessExit(exitCode);
            if (!string.IsNullOrWhiteSpace(category))
            {
                parts.Add($"Failure category: {category}");
            }

            if (exitCode == 0 || _owner._sawBlenderQuitLine)
            {
                parts.Add("Blender worker exited normally.");
                if (_owner._sawBlenderQuitLine)
                {
                    parts.Add("Observed 'Blender quit' in the process output.");
                }
            }
            else
            {
                parts.Add($"Blender worker exited unexpectedly with code {exitCode}.");
            }

            var crashReportPath = FindCrashReportPath();
            if (!string.IsNullOrWhiteSpace(crashReportPath))
            {
                parts.Add($"Crash report: {crashReportPath}");
            }

            var recentTail = GetRecentOutputTail(12);
            if (!string.IsNullOrWhiteSpace(recentTail))
            {
                parts.Add("Recent Blender output:");
                parts.Add(recentTail);
            }

            return string.Join(Environment.NewLine, parts);
        }

        public string ClassifyProcessExit(int exitCode)
        {
            var recentTail = GetRecentOutputTail(20);
            var classifiedFromOutput = ClassifyErrorText(recentTail);
            if (!string.IsNullOrWhiteSpace(classifiedFromOutput))
            {
                return classifiedFromOutput;
            }

            return exitCode == 0 || _owner._sawBlenderQuitLine
                ? "normal_quit"
                : "unexpected_exit";
        }

        public static string ClassifyErrorText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.ToLowerInvariant();
            if (normalized.Contains("file format is not supported") ||
                normalized.Contains("unable to open blend file") ||
                normalized.Contains("cannot read file as a blender file") ||
                normalized.Contains("not a blend file"))
            {
                return "file_error";
            }

            if (normalized.Contains("traceback") ||
                normalized.Contains("runtimeerror:") ||
                normalized.Contains("syntaxerror:") ||
                normalized.Contains("nameerror:") ||
                normalized.Contains("valueerror:") ||
                normalized.Contains("unknown worker command"))
            {
                return "script_error";
            }

            if (normalized.Contains("blender quit"))
            {
                return "normal_quit";
            }

            return string.Empty;
        }

        public string GetRecentOutputTail(int maxLines)
        {
            lock (_owner._recentOutputLock)
            {
                if (_owner._recentOutputLines.Count == 0)
                {
                    return string.Empty;
                }

                return string.Join(
                    Environment.NewLine,
                    _owner._recentOutputLines.Skip(Math.Max(0, _owner._recentOutputLines.Count - maxLines)));
            }
        }

        public string FindCrashReportPath()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var diagnosticReports = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library",
                        "Logs",
                        "DiagnosticReports");

                    return FindNewestCrashFile(
                        diagnosticReports,
                        ["Blender*.crash", "Blender*.ips"],
                        _owner._processStartedAtUtc);
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var candidates = new List<string>();
                    if (!string.IsNullOrWhiteSpace(_owner._lastLoadedBlendFilePath))
                    {
                        var blendDirectory = Path.GetDirectoryName(_owner._lastLoadedBlendFilePath);
                        var blendName = Path.GetFileNameWithoutExtension(_owner._lastLoadedBlendFilePath);
                        if (!string.IsNullOrWhiteSpace(blendDirectory) && !string.IsNullOrWhiteSpace(blendName))
                        {
                            candidates.Add(Path.Combine(blendDirectory, blendName + ".crash.txt"));
                        }
                    }

                    candidates.Add("/tmp/blender.crash.txt");
                    foreach (var candidate in candidates.Distinct())
                    {
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }

                    return FindNewestCrashFile("/tmp", ["*.crash.txt"], _owner._processStartedAtUtc);
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        public static string FindNewestCrashFile(string directory, IReadOnlyList<string> patterns,
            DateTimeOffset processStartedAtUtc)
        {
            if (!Directory.Exists(directory))
            {
                return string.Empty;
            }

            var minTimestamp = processStartedAtUtc.AddMinutes(-1);
            var newest = patterns
                .SelectMany(pattern =>
                {
                    try
                    {
                        return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly);
                    }
                    catch
                    {
                        return Enumerable.Empty<string>();
                    }
                })
                .Select(path => new FileInfo(path))
                .Where(info => info.Exists && info.LastWriteTimeUtc >= minTimestamp.UtcDateTime)
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault();

            return newest?.FullName ?? string.Empty;
        }
    }
}
