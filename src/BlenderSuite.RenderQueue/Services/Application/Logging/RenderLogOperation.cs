using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BlenderSuite.RenderQueue.Services.Application.Logging;

public sealed class RenderLogOperation
{
    private readonly IRenderLogService _logService;
    private readonly RenderLogScope _scope;
    private readonly string _operationName;
    private readonly string _source;
    private readonly Guid? _taskId;
    private readonly string? _blendFilePath;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    internal RenderLogOperation(
        IRenderLogService logService,
        RenderLogScope scope,
        string operationName,
        string source,
        string startMessage,
        Guid? taskId,
        string? blendFilePath,
        IReadOnlyDictionary<string, string>? metadata)
    {
        _logService = logService;
        _scope = scope;
        _operationName = operationName;
        _source = source;
        _taskId = taskId;
        _blendFilePath = blendFilePath;
        OperationId = Guid.NewGuid();

        Detail(startMessage, metadata: metadata, phase: RenderLogMetadata.PhaseStart);
    }

    public Guid OperationId { get; }

    public void Detail(
        string message,
        RenderLogLevel level = RenderLogLevel.Info,
        IReadOnlyDictionary<string, string>? metadata = null,
        string phase = RenderLogMetadata.PhaseDetail)
    {
        Write(level, message, phase, RenderLogMetadata.AudienceDiagnostic, metadata, includeDuration: false);
    }

    public void Complete(
        string message,
        RenderLogLevel level = RenderLogLevel.Info,
        IReadOnlyDictionary<string, string>? metadata = null,
        string audience = RenderLogMetadata.AudienceUser)
    {
        Write(level, message, RenderLogMetadata.PhaseSuccess, audience, metadata, includeDuration: true);
    }

    public void Fail(
        string message,
        IReadOnlyDictionary<string, string>? metadata = null,
        string audience = RenderLogMetadata.AudienceUser)
    {
        Write(RenderLogLevel.Error, message, RenderLogMetadata.PhaseError, audience, metadata, includeDuration: true);
    }

    private void Write(
        RenderLogLevel level,
        string message,
        string phase,
        string audience,
        IReadOnlyDictionary<string, string>? metadata,
        bool includeDuration)
    {
        _logService.Write(
            level,
            _scope,
            message,
            _taskId,
            _blendFilePath,
            _source,
            RenderLogMetadata.ForOperation(
                metadata,
                OperationId,
                _operationName,
                phase,
                audience,
                includeDuration ? _stopwatch.ElapsedMilliseconds : null));
    }
}

public static class RenderLogOperationExtensions
{
    public static RenderLogOperation BeginOperation(
        this IRenderLogService logService,
        RenderLogScope scope,
        string operationName,
        string source,
        string startMessage,
        Guid? taskId = null,
        string? blendFilePath = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new RenderLogOperation(
            logService,
            scope,
            operationName,
            source,
            startMessage,
            taskId,
            blendFilePath,
            metadata);
    }
}
