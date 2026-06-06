using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using BlenderSuite.RenderQueue.Extensions;
using BlenderSuite.RenderQueue.Helpers;
using BlenderSuite.RenderQueue.Localizer;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.Business.Blender;
using BlenderSuite.RenderQueue.Services.UI;
using BlenderSuite.RenderQueue.ViewModels.Logs;

namespace BlenderSuite.RenderQueue.ViewModels;

public partial class RenderTaskViewModel
{
    private sealed class RenderTaskFilePropertiesCoordinator(RenderTaskViewModel owner)
    {
        private readonly RenderTaskViewModel _owner = owner;

        public async Task RefreshAsync(string blenderPath)
        {
            if (string.IsNullOrWhiteSpace(_owner.BlendFilePath) || !File.Exists(_owner.BlendFilePath))
            {
                _owner.EnqueueLog("文件路径无效或文件不存在，无法刷新");
                return;
            }

            try
            {
                _owner.EnqueueLog("[REFRESH] 开始刷新文件属性...");
                _owner._logService?.Write(
                    RenderLogLevel.Info,
                    RenderLogScope.Task,
                    $"开始刷新文件属性，Blender={blenderPath}, 文件={_owner.BlendFilePath}, IsLoading={_owner.ScenePropertiesView.IsLoading}, IsLoaded={_owner.ScenePropertiesView.SelectedSceneProperties.IsLoaded}",
                    _owner.Id,
                    _owner.BlendFilePath,
                    nameof(RenderTaskViewModel));

                var currentOverrideFrameRange = _owner.OverrideFrameRange;
                var currentStartFrame = _owner.StartFrame;
                var currentEndFrame = _owner.EndFrame;
                var currentOverrideScene = _owner.OverrideScene;
                var currentSelectedSceneName = _owner.SelectedSceneName;
                var currentEnable = _owner.Enable;

                _owner._logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Refreshing file properties - preserving overrides: FrameRange={currentOverrideFrameRange} ({currentStartFrame}-{currentEndFrame}), Scene={currentOverrideScene} ({currentSelectedSceneName}), Enable={currentEnable}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");

                await _owner.ScenePropertiesView.LoadPropertiesAsync(blenderPath, _owner.BlendFilePath);

                _owner.OverrideFrameRange = currentOverrideFrameRange;
                _owner.StartFrame = currentStartFrame;
                _owner.EndFrame = currentEndFrame;
                _owner.OverrideScene = currentOverrideScene;
                _owner.SelectedSceneName = currentSelectedSceneName;
                _owner.Enable = currentEnable;

                ApplyLoadedSceneState(syncAnimationFromSceneRange: false);
                _owner.LoadFileInfo();

                _owner.EnqueueLog("[REFRESH] 文件属性刷新完成");
                _owner._logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, "文件属性刷新完成。", _owner.Id,
                    _owner.BlendFilePath, nameof(RenderTaskViewModel));
                _owner._logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, "✅ File properties refreshed successfully - overrides preserved", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
            }
            catch (Exception ex)
            {
                _owner.EnqueueLog($"[REFRESH] 刷新文件属性失败: {ex.Message}");
                _owner._logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"刷新文件属性失败: {ex}",
                    _owner.Id, _owner.BlendFilePath, nameof(RenderTaskViewModel));
                _owner._logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"❌ Failed to refresh file properties: {ex.Message}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
            }
        }

        public async Task LoadAsync(string blenderPath)
        {
            if (string.IsNullOrWhiteSpace(_owner.BlendFilePath) || !File.Exists(_owner.BlendFilePath))
            {
                _owner.EnqueueLog("文件路径无效或文件不存在");
                return;
            }

            try
            {
                _owner.EnqueueLog("[QUERY] 开始加载文件属性...");
                await _owner.ScenePropertiesView.LoadPropertiesAsync(blenderPath, _owner.BlendFilePath);

                _owner.EnqueueLog(_owner.OverrideFrameRange
                    ? $"[QUERY] 文件属性加载完成: 使用覆写帧范围 {_owner.StartFrame}..{_owner.EndFrame}"
                    : $"[QUERY] 文件属性加载完成: 使用场景默认帧范围 {_owner.ScenePropertiesView.SceneProperties.FrameStart}..{_owner.ScenePropertiesView.SceneProperties.FrameEnd}");

                ApplyLoadedSceneState(syncAnimationFromSceneRange: true);
            }
            catch (Exception ex)
            {
                _owner.EnqueueLog($"[QUERY] 加载文件属性失败: {ex.Message}");
            }
        }

        private void ApplyLoadedSceneState(bool syncAnimationFromSceneRange)
        {
            _owner.AvailableSceneNames = _owner.ScenePropertiesView.SceneNames.ToList();

            if (!_owner.OverrideScene && string.IsNullOrEmpty(_owner.SelectedSceneName))
            {
                _owner.SelectedSceneName = _owner.ScenePropertiesView.SelectedSceneName ?? string.Empty;
            }

            _owner.OnPropertyChanged(nameof(DisplayStartFrame));
            _owner.OnPropertyChanged(nameof(DisplayEndFrame));
            _owner.OnPropertyChanged(nameof(DisplayTotalFrames));
            _owner.OnPropertyChanged(nameof(RealStartFrame));
            _owner.OnPropertyChanged(nameof(RealEndFrame));
            _owner.OnPropertyChanged(nameof(RealTotalFrames));
            if (syncAnimationFromSceneRange)
            {
                _owner.Animation = _owner.RealStartFrame != _owner.RealEndFrame;
            }
            _owner.OnPropertyChanged(nameof(AvailableSceneNames));
            _owner.OnPropertyChanged(nameof(HasValidSceneSelection));
            _owner.OnPropertyChanged(nameof(ShowSceneOverrideWarning));
            _owner.OnPropertyChanged(nameof(IsOverrideSceneIsDefaultScene));
            _owner.OnPropertyChanged(nameof(FinalSceneProperties));
            _owner.OnPropertyChanged(nameof(FramePathDirectory));
        }
    }

    private sealed class RenderTaskPreviewService(RenderTaskViewModel owner)
    {
        private readonly RenderTaskViewModel _owner = owner;

        public async Task LoadRenderedImageAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    return;
                }

                var bitmap = await Task.Run(() =>
                {
                    try
                    {
                        using var fileStream = File.OpenRead(imagePath);
                        return new Bitmap(fileStream);
                    }
                    catch (Exception ex)
                    {
                        _owner._logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Error loading image: {ex.Message}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
                        _owner._logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Stack trace: {ex.StackTrace}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
                        return null;
                    }
                });

                if (bitmap == null)
                {
                    return;
                }

                bitmap.Dispose();

                Task.Run(async () =>
                {
                    try
                    {
                        var optimizedBitmap = await LoadAndOptimizeImageAsync(imagePath, 120, 90);

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (optimizedBitmap != null)
                                {
                                    _owner.RenderedImage?.Dispose();
                                    _owner.RenderedImage = optimizedBitmap;
                                    _owner.RenderedImagePath = imagePath;
                                    _owner.HasRenderedImage = true;
                                }
                                else
                                {
                                    _owner.HasRenderedImage = false;
                                }
                            }
                            catch (Exception ex)
                            {
                                _owner._logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Error setting optimized image: {ex.Message}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
                                _owner.HasRenderedImage = false;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _owner._logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Error loading optimized image: {ex.Message}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
                    }
                }).FireAndForget(
                    _owner._logService,
                    nameof(RenderTaskViewModel),
                    RenderLogScope.Task,
                    "后台优化渲染图片失败。");
            }
            catch (Exception ex)
            {
                _owner._logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Error in LoadRenderedImageAsync: {ex.Message}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
            }
        }

        public async Task<Bitmap?> LoadAndOptimizeImageAsync(string imagePath, int maxWidth, int maxHeight)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var fileStream = File.OpenRead(imagePath);
                    var originalBitmap = new Bitmap(fileStream);
                    var originalSize = originalBitmap.PixelSize;

                    if (originalSize.Width <= maxWidth && originalSize.Height <= maxHeight)
                    {
                        return originalBitmap;
                    }

                    var scaleX = (double)maxWidth / originalSize.Width;
                    var scaleY = (double)maxHeight / originalSize.Height;
                    var scale = Math.Min(scaleX, scaleY);

                    var newWidth = (int)(originalSize.Width * scale);
                    var newHeight = (int)(originalSize.Height * scale);

                    var renderTarget = new RenderTargetBitmap(new Avalonia.PixelSize(newWidth, newHeight));
                    using (var drawingContext = renderTarget.CreateDrawingContext())
                    {
                        var sourceRect = new Avalonia.Rect(0, 0, originalSize.Width, originalSize.Height);
                        var destRect = new Avalonia.Rect(0, 0, newWidth, newHeight);
                        drawingContext.DrawImage(originalBitmap, sourceRect, destRect);
                    }

                    originalBitmap.Dispose();
                    return renderTarget;
                }
                catch (Exception ex)
                {
                    _owner._logService?.Write(RenderLogLevel.Error, RenderLogScope.Task, $"Error loading and optimizing image: {ex.Message}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
                    _owner._logService?.Write(RenderLogLevel.Info, RenderLogScope.Task, $"Stack trace: {ex.StackTrace}", _owner.Id, _owner.BlendFilePath, "RenderTaskViewModel");
                    return null;
                }
            });
        }
    }

    private sealed class RenderTaskLogProjection(RenderTaskViewModel owner)
    {
        private readonly RenderTaskViewModel _owner = owner;

        public void Clear()
        {
            _owner._logClearCutoff = DateTimeOffset.UtcNow;
            _owner.TimelineEntries.Clear();
            _owner.DebugEntries.Clear();
            _owner.DebugLogText = string.Empty;
            _owner.OutputLog = string.Empty;
            _owner.OnPropertyChanged(nameof(HasTimelineEntries));
            _owner.OnPropertyChanged(nameof(HasDebugEntries));
        }

        public void Enqueue(string line)
        {
            if (_owner.IsLogPaused || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var (level, scope, message, audience, kind) = ClassifyLogLine(line);
            if (_owner._logService != null)
            {
                _owner._logService.Write(
                    level,
                    scope,
                    message,
                    _owner.Id,
                    _owner.BlendFilePath,
                    nameof(RenderTaskViewModel),
                    new Dictionary<string, string>
                    {
                        ["audience"] = audience,
                        ["kind"] = kind
                    });
                return;
            }

            var fallbackLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (string.Equals(audience, "debug", StringComparison.Ordinal))
            {
                _owner.DebugLogText = string.IsNullOrWhiteSpace(_owner.DebugLogText)
                    ? fallbackLine
                    : $"{_owner.DebugLogText}{Environment.NewLine}{fallbackLine}";
                _owner.OutputLog = _owner.DebugLogText;
            }
        }

        public void Attach(IRenderLogService logService)
        {
            if (ReferenceEquals(_owner._logService, logService))
            {
                return;
            }

            Detach();
            _owner._logService = logService;
            _owner._logService.LogAppended += _owner.OnLogAppended;
            Rebuild();
        }

        public void Detach()
        {
            if (_owner._logService == null)
            {
                return;
            }

            _owner._logService.LogAppended -= _owner.OnLogAppended;
            _owner._logService = null;
        }

        public void OnLogAppended(RenderLogEvent logEvent)
        {
            if (!ShouldIncludeForTask(logEvent))
            {
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!ShouldIncludeForTask(logEvent))
                {
                    return;
                }

                if (ShouldIncludeInTimeline(logEvent))
                {
                    _owner.TimelineEntries.Insert(0, new TaskLogEntryViewModel(logEvent));
                }

                if (ShouldIncludeInDebug(logEvent))
                {
                    var debugEntry = new TaskLogEntryViewModel(logEvent);
                    _owner.DebugEntries.Insert(0, debugEntry);
                    AppendDebugText(debugEntry);
                }

                _owner.OnPropertyChanged(nameof(HasTimelineEntries));
                _owner.OnPropertyChanged(nameof(HasDebugEntries));
            });
        }

        public void Rebuild()
        {
            _owner.TimelineEntries.Clear();
            _owner.DebugEntries.Clear();
            _owner.DebugLogText = string.Empty;
            _owner.OutputLog = string.Empty;

            if (_owner._logService == null)
            {
                return;
            }

            var events = _owner._logService.GetEvents(new RenderLogProjection
            {
                TaskId = _owner.Id,
                IncludeDebug = true,
                IncludeRaw = true
            });

            foreach (var logEvent in events.Reverse())
            {
                if (!ShouldIncludeForTask(logEvent))
                {
                    continue;
                }

                if (ShouldIncludeInTimeline(logEvent))
                {
                    _owner.TimelineEntries.Insert(0, new TaskLogEntryViewModel(logEvent));
                }

                if (ShouldIncludeInDebug(logEvent))
                {
                    _owner.DebugEntries.Insert(0, new TaskLogEntryViewModel(logEvent));
                }
            }

            RefreshDebugText();
            _owner.OnPropertyChanged(nameof(HasTimelineEntries));
            _owner.OnPropertyChanged(nameof(HasDebugEntries));
        }

        private bool ShouldIncludeForTask(RenderLogEvent logEvent)
        {
            if (logEvent.TaskId != _owner.Id)
            {
                return false;
            }

            return !_owner._logClearCutoff.HasValue || logEvent.Timestamp >= _owner._logClearCutoff.Value;
        }

        private static bool ShouldIncludeInTimeline(RenderLogEvent logEvent)
        {
            if (logEvent.Level == RenderLogLevel.Debug)
            {
                return false;
            }

            return !logEvent.Metadata.TryGetValue("audience", out var audience) ||
                   !string.Equals(audience, "debug", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldIncludeInDebug(RenderLogEvent logEvent)
        {
            if (logEvent.Level == RenderLogLevel.Debug)
            {
                return true;
            }

            return logEvent.Metadata.TryGetValue("audience", out var audience) &&
                   string.Equals(audience, "debug", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshDebugText()
        {
            _owner.DebugLogText = string.Join(
                Environment.NewLine,
                _owner.DebugEntries
                    .OrderBy(entry => entry.Event.Timestamp)
                    .Select(FormatDebugLine));
            _owner.OutputLog = _owner.DebugLogText;
        }

        private void AppendDebugText(TaskLogEntryViewModel entry)
        {
            var line = FormatDebugLine(entry);
            _owner.DebugLogText = string.IsNullOrEmpty(_owner.DebugLogText)
                ? line
                : string.Concat(_owner.DebugLogText, Environment.NewLine, line);
            _owner.OutputLog = _owner.DebugLogText;
        }

        private static string FormatDebugLine(TaskLogEntryViewModel entry)
        {
            return $"[{entry.Event.Timestamp.ToLocalTime():HH:mm:ss}] [{entry.LevelText}] {entry.Message}";
        }

        private static (RenderLogLevel Level, RenderLogScope Scope, string Message, string Audience, string Kind)
            ClassifyLogLine(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                return (RenderLogLevel.Error, RenderLogScope.System, trimmed[7..].Trim(), "timeline", "message");
            }

            if (trimmed.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase))
            {
                return (RenderLogLevel.Warning, RenderLogScope.System, trimmed[6..].Trim(), "debug", "message");
            }

            if (trimmed.StartsWith("[QUERY]", StringComparison.OrdinalIgnoreCase))
            {
                return (RenderLogLevel.Info, RenderLogScope.Task, trimmed[7..].Trim(), "timeline", "message");
            }

            if (trimmed.StartsWith("[REFRESH]", StringComparison.OrdinalIgnoreCase))
            {
                return (RenderLogLevel.Info, RenderLogScope.Task, trimmed[9..].Trim(), "timeline", "message");
            }

            if (trimmed.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase))
            {
                return (RenderLogLevel.Info, RenderLogScope.System, trimmed[6..].Trim(), "timeline", "message");
            }

            return (RenderLogLevel.Info, RenderLogScope.Task, trimmed, "timeline", "message");
        }
    }

    private sealed class RenderTaskVideoGenerationCoordinator(RenderTaskViewModel owner)
    {
        private readonly RenderTaskViewModel _owner = owner;

        public async Task GenerateVideoAsync()
        {
            try
            {
                if (_owner._processService == null)
                {
                    _owner.EnqueueLog("[ERROR] " + Localizer.Localizer.Instance["VideoGeneration_ServiceUnavailable"]);
                    _owner.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                        Localizer.Localizer.Instance["VideoGeneration_ServiceUnavailable"]);
                    return;
                }

                var framePath = _owner.FinalSceneProperties.FramePath;
                if (string.IsNullOrEmpty(framePath))
                {
                    _owner.EnqueueLog("[ERROR] " + Localizer.Localizer.Instance["VideoGeneration_NoFramePath"]);
                    _owner.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                        Localizer.Localizer.Instance["VideoGeneration_NoFramePath"]);
                    return;
                }

                var frameDirectory = Path.GetDirectoryName(framePath);
                if (string.IsNullOrEmpty(frameDirectory) || !Directory.Exists(frameDirectory))
                {
                    _owner.EnqueueLog("[ERROR] " +
                                      string.Format(Localizer.Localizer.Instance["VideoGeneration_FramePathNotExists"],
                                          frameDirectory));
                    _owner.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                        string.Format(Localizer.Localizer.Instance["VideoGeneration_FramePathNotExists"],
                            frameDirectory));
                    return;
                }

                var supportedExtensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tiff", "*.tga" };
                var hasImages = supportedExtensions.Any(ext =>
                    Directory.GetFiles(frameDirectory, ext, SearchOption.TopDirectoryOnly).Length > 0);

                if (!hasImages)
                {
                    _owner.EnqueueLog("[ERROR] " +
                                      string.Format(Localizer.Localizer.Instance["VideoGeneration_NoImagesInFramePath"],
                                          frameDirectory));
                    _owner.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                        string.Format(Localizer.Localizer.Instance["VideoGeneration_NoImagesInFramePath"],
                            frameDirectory));
                    return;
                }

                var fps = _owner.FinalSceneProperties.Fps ?? 24.0;
                var inputDirectoryName = Path.GetFileName(frameDirectory);
                var parentDirectory = Path.GetDirectoryName(frameDirectory);
                var outputVideoPath = Path.Combine(parentDirectory ?? string.Empty, $"{inputDirectoryName}.mp4");

                _owner.IsGeneratingVideo = true;
                _owner.VideoGenerationProgress = 0.0;
                _owner.VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Starting"];
                _owner.EnqueueLog(string.Format(Localizer.Localizer.Instance["VideoGeneration_LogStarting"],
                    outputVideoPath));

                var progressBar = new ProgressBar
                {
                    Value = 0,
                    ShowProgressText = true,
                    Minimum = 0,
                    Maximum = 100
                };
                var fileName = Path.GetFileName(_owner.BlendFilePath);
                var titleName = fileName.EndsWith(".blend", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^6]
                    : fileName;
                var progressToast = _owner.ShowProgressToast(
                    string.Format(Localizer.Localizer.Instance["VideoGeneration_ToastTitle"], titleName),
                    progressBar);

                var videoProcess = await _owner._processService.CreateVideoProcessAsync();
                var success = false;

                try
                {
                    var tempVideoService = new BlenderVideoService(videoProcess, _owner._logService);
                    success = await tempVideoService.GenerateVideoFromImagesAsync(
                        frameDirectory,
                        outputVideoPath,
                        fps,
                        _owner._videoCodec,
                        _owner._videoQuality,
                        progress =>
                        {
                            _owner.VideoGenerationProgress = progress;
                            _owner.VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Generating"];
                            progressToast?.UpdateProgressToast(progress);
                        });
                }
                finally
                {
                    await videoProcess.StopAsync();
                    _owner._processService.UnregisterProcess(videoProcess.ProcessId);
                    videoProcess.Dispose();
                }

                if (success)
                {
                    _owner.VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Completed"];
                    _owner.EnqueueLog(string.Format(Localizer.Localizer.Instance["VideoGeneration_LogSuccess"],
                        outputVideoPath));

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _owner.DismissToast(progressToast);
                        _owner.ShowSuccessToast(Localizer.Localizer.Instance["VideoGeneration_SuccessTitle"],
                            string.Format(Localizer.Localizer.Instance["VideoGeneration_SuccessMessage"],
                                Path.GetFileName(_owner.BlendFilePath)));
                    });

                    if (!string.IsNullOrEmpty(outputVideoPath) && File.Exists(outputVideoPath))
                    {
                        _ = Task.Delay(1000).ContinueWith(_ =>
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                var opened = FileSystemHelper.OpenFileDirectory(outputVideoPath);
                                if (!opened)
                                {
                                    _owner.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_OpenFailed"],
                                        Localizer.Localizer.Instance["VideoGeneration_CannotOpenLocation"]);
                                }
                            });
                        });
                    }
                }
                else
                {
                    _owner.VideoGenerationStatus = Localizer.Localizer.Instance["VideoGeneration_Failed"];
                    _owner.EnqueueLog(Localizer.Localizer.Instance["VideoGeneration_LogFailed"]);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _owner.DismissToast(progressToast);
                        _owner.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                            Localizer.Localizer.Instance["VideoGeneration_ErrorMessage"]);
                    });
                }
            }
            catch (Exception ex)
            {
                _owner.VideoGenerationStatus =
                    string.Format(Localizer.Localizer.Instance["VideoGeneration_Error"], ex.Message);
                _owner.EnqueueLog(string.Format(Localizer.Localizer.Instance["VideoGeneration_LogError"], ex.Message));

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _owner.ShowErrorToast(Localizer.Localizer.Instance["VideoGeneration_FailedTitle"],
                        string.Format(Localizer.Localizer.Instance["VideoGeneration_ErrorMessageWithDetails"],
                            ex.Message));
                });
            }
            finally
            {
                _owner.IsGeneratingVideo = false;
            }
        }
    }
}
