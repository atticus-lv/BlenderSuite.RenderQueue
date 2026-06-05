using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

public sealed record BlenderValidationRequest(
    string Path,
    string Channel,
    int RequestVersion,
    CancellationToken CancellationToken);

public enum BlenderValidationStatus
{
    Success,
    EmptyPath,
    FileNotFound,
    Canceled,
    Error,
    Stale
}

public sealed class BlenderValidationResult
{
    public BlenderValidationStatus Status { get; init; }
    public string Path { get; init; } = string.Empty;
    public int RequestVersion { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsCanceled { get; init; }
    public BlenderVersionInfo? VersionInfo { get; init; }
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}

public sealed class BlenderValidationService : IBlenderValidationService, IDisposable
{
    public const string DefaultChannel = "default";

    private readonly IBlenderCliInfoService _cliInfoService;
    private readonly object _gate = new();
    private readonly Dictionary<string, ValidationState> _states = new(StringComparer.Ordinal);

    public BlenderValidationService(IBlenderCliInfoService cliInfoService)
    {
        _cliInfoService = cliInfoService;
    }

    public BlenderValidationRequest BeginValidation(string? path, string channel = DefaultChannel)
    {
        lock (_gate)
        {
            var state = GetOrCreateState(channel);
            state.CurrentCts?.Cancel();
            state.CurrentCts?.Dispose();
            state.CurrentCts = new CancellationTokenSource();
            var requestVersion = Interlocked.Increment(ref state.RequestVersion);
            return new BlenderValidationRequest(path ?? string.Empty, channel, requestVersion, state.CurrentCts.Token);
        }
    }

    public async Task<BlenderValidationResult> ValidateAsync(
        BlenderValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            request.CancellationToken,
            cancellationToken);
        return await ValidateCoreAsync(
            request.Path,
            request.Channel,
            request.RequestVersion,
            linkedCts.Token,
            trackCurrent: true);
    }

    public BlenderValidationResult? ValidatePreconditions(BlenderValidationRequest request)
    {
        return CreatePreconditionResult(request.Path, request.Channel, request.RequestVersion, trackCurrent: true);
    }

    public Task<BlenderValidationResult> ValidatePathAsync(string? path, CancellationToken cancellationToken = default)
    {
        return ValidateCoreAsync(
            path ?? string.Empty,
            DefaultChannel,
            requestVersion: 0,
            cancellationToken,
            trackCurrent: false);
    }

    public bool IsCurrent(BlenderValidationRequest request)
    {
        lock (_gate)
        {
            return _states.TryGetValue(request.Channel, out var state) &&
                   state.RequestVersion == request.RequestVersion &&
                   !request.CancellationToken.IsCancellationRequested;
        }
    }

    public void CancelCurrent(string channel = DefaultChannel)
    {
        lock (_gate)
        {
            var state = GetOrCreateState(channel);
            state.CurrentCts?.Cancel();
            Interlocked.Increment(ref state.RequestVersion);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var state in _states.Values)
            {
                state.CurrentCts?.Cancel();
                state.CurrentCts?.Dispose();
            }

            _states.Clear();
        }
    }

    private async Task<BlenderValidationResult> ValidateCoreAsync(
        string path,
        string channel,
        int requestVersion,
        CancellationToken cancellationToken,
        bool trackCurrent)
    {
        var preconditionResult = CreatePreconditionResult(path, channel, requestVersion, trackCurrent);
        if (preconditionResult != null)
        {
            return preconditionResult;
        }

        try
        {
            var info = await _cliInfoService.GetVersionInfoAsync(path, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateResult(BlenderValidationStatus.Canceled, path, channel, requestVersion, trackCurrent, "验证已取消");
            }

            if (trackCurrent && !IsRequestVersionCurrent(channel, requestVersion))
            {
                return CreateResult(BlenderValidationStatus.Stale, path, channel, requestVersion, trackCurrent, "验证请求已过期");
            }

            return CreateResult(
                BlenderValidationStatus.Success,
                path,
                channel,
                requestVersion,
                trackCurrent,
                string.Empty,
                versionInfo: info);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(BlenderValidationStatus.Canceled, path, channel, requestVersion, trackCurrent, "验证已取消", exception: ex);
        }
        catch (Exception ex)
        {
            return CreateResult(BlenderValidationStatus.Error, path, channel, requestVersion, trackCurrent, ex.Message, exception: ex);
        }
    }

    private BlenderValidationResult? CreatePreconditionResult(
        string path,
        string channel,
        int requestVersion,
        bool trackCurrent)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateResult(BlenderValidationStatus.EmptyPath, path, channel, requestVersion, trackCurrent, "Blender路径为空");
        }

        if (!File.Exists(path))
        {
            return CreateResult(BlenderValidationStatus.FileNotFound, path, channel, requestVersion, trackCurrent, "指定的文件不存在");
        }

        return null;
    }

    private BlenderValidationResult CreateResult(
        BlenderValidationStatus status,
        string path,
        string channel,
        int requestVersion,
        bool trackCurrent,
        string message,
        BlenderVersionInfo? versionInfo = null,
        Exception? exception = null)
    {
        var isCanceled = status == BlenderValidationStatus.Canceled;
        var isCurrent = !trackCurrent || IsRequestVersionCurrent(channel, requestVersion);
        return new BlenderValidationResult
        {
            Status = trackCurrent && !isCurrent && status != BlenderValidationStatus.Canceled
                ? BlenderValidationStatus.Stale
                : status,
            Path = path,
            RequestVersion = requestVersion,
            IsCurrent = isCurrent,
            IsCanceled = isCanceled,
            VersionInfo = versionInfo,
            Message = message,
            Exception = exception
        };
    }

    private ValidationState GetOrCreateState(string channel)
    {
        if (_states.TryGetValue(channel, out var state))
        {
            return state;
        }

        state = new ValidationState();
        _states[channel] = state;
        return state;
    }

    private bool IsRequestVersionCurrent(string channel, int requestVersion)
    {
        lock (_gate)
        {
            return _states.TryGetValue(channel, out var state) && state.RequestVersion == requestVersion;
        }
    }

    private sealed class ValidationState
    {
        public CancellationTokenSource? CurrentCts { get; set; }
        public int RequestVersion;
    }
}
