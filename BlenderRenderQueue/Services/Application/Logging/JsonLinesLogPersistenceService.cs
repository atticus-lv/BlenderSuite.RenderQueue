using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlenderRenderQueue.Services.Business.Submission;

namespace BlenderRenderQueue.Services.Application.Logging;

[JsonSerializable(typeof(RenderLogEvent))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class RenderLogJsonContext : JsonSerializerContext
{
}

public sealed class JsonLinesLogPersistenceService : ILogPersistenceService
{
    private const int MaxRetainedSessions = 50;
    private static readonly TimeSpan MaxRetainedAge = TimeSpan.FromDays(30);
    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = RenderLogJsonContext.Default
    };

    private readonly string _sessionsDirectory;
    private readonly string _sessionFilePath;

    public JsonLinesLogPersistenceService()
    {
        CurrentSessionId = Guid.NewGuid().ToString("N");
        _sessionsDirectory = Path.Combine(SubmissionPaths.GetAppDataDirectory(), "Logs", "Sessions");
        Directory.CreateDirectory(_sessionsDirectory);
        CleanupRetention();
        _sessionFilePath = Path.Combine(_sessionsDirectory,
            $"{DateTime.UtcNow:yyyyMMddTHHmmss}-{CurrentSessionId}.jsonl");
    }

    public string CurrentSessionId { get; }

    public IReadOnlyList<RenderLogEvent> LoadAll()
    {
        lock (_syncRoot)
        {
            var files = Directory.Exists(_sessionsDirectory)
                ? Directory.GetFiles(_sessionsDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
                : [];

            var events = new List<RenderLogEvent>();
            foreach (var file in files.OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var logEvent = JsonSerializer.Deserialize(line, RenderLogJsonContext.Default.RenderLogEvent);
                        if (logEvent != null)
                        {
                            events.Add(logEvent);
                        }
                    }
                }
                catch
                {
                    // Ignore unreadable history files and keep loading the rest.
                }
            }

            return events.OrderBy(e => e.Timestamp).ToList();
        }
    }

    public void Append(RenderLogEvent logEvent)
    {
        lock (_syncRoot)
        {
            var line = JsonSerializer.Serialize(logEvent, RenderLogJsonContext.Default.RenderLogEvent);
            File.AppendAllText(_sessionFilePath, line + Environment.NewLine);
        }
    }

    public void ClearAll()
    {
        lock (_syncRoot)
        {
            if (!Directory.Exists(_sessionsDirectory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(_sessionsDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    public void ClearHistory()
    {
        lock (_syncRoot)
        {
            if (!Directory.Exists(_sessionsDirectory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(_sessionsDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(file, _sessionFilePath, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    private void CleanupRetention()
    {
        if (!Directory.Exists(_sessionsDirectory))
        {
            return;
        }

        var files = new DirectoryInfo(_sessionsDirectory)
            .EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        var expiry = DateTime.UtcNow - MaxRetainedAge;
        foreach (var file in files.Where(file => file.LastWriteTimeUtc < expiry))
        {
            TryDelete(file);
        }

        files = new DirectoryInfo(_sessionsDirectory)
            .EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var file in files.Skip(MaxRetainedSessions))
        {
            TryDelete(file);
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
            // ignored
        }
    }
}
