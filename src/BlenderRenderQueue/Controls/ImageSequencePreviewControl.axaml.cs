using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;
using BlenderRenderQueue.Extensions;
using BlenderRenderQueue.Views;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Services.UI;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.Controls;

// 简单的Observer实现
public class Observer<T> : IObserver<T>
{
    private readonly Action<T> _onNext;

    public Observer(Action<T> onNext)
    {
        _onNext = onNext;
    }

    public void OnNext(T value) => _onNext(value);

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }
}

public partial class ImageSequencePreviewControl : UserControl, IDisposable
{
    private string? _folderPath;
    private Bitmap? _currentImage;
    private bool _isLoading;
    private int _currentFrame;
    private int _maxFrame;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private bool _hasImages;
    private List<string> _imageFiles = [];
    private Bitmap?[] _imageCache = [];
    private FileSystemWatcher? _fileWatcher;
    private readonly SemaphoreSlim _sequenceLoadLock = new(1, 1);
    private CancellationTokenSource? _refreshDebounceCts;
    private int _loadRequestVersion;
    private int _imageLoadRequestVersion;
    private bool _disposed;
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png"];

    public static readonly DirectProperty<ImageSequencePreviewControl, string?> FolderPathProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, string?>(
            nameof(FolderPath),
            o => o.FolderPath,
            (o, v) => o.FolderPath = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, Bitmap?> CurrentImageProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, Bitmap?>(
            nameof(CurrentImage),
            o => o.CurrentImage,
            (o, v) => o.CurrentImage = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, bool>(
            nameof(IsLoading),
            o => o.IsLoading,
            (o, v) => o.IsLoading = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, int> CurrentFrameProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, int>(
            nameof(CurrentFrame),
            o => o.CurrentFrame,
            (o, v) => o.CurrentFrame = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, int> MaxFrameProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, int>(
            nameof(MaxFrame),
            o => o.MaxFrame,
            (o, v) => o.MaxFrame = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, bool> HasImagesProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, bool>(
            nameof(HasImages),
            o => o.HasImages,
            (o, v) => o.HasImages = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, bool> HasErrorProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, bool>(
            nameof(HasError),
            o => o.HasError,
            (o, v) => o.HasError = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, string> ErrorMessageProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, string>(
            nameof(ErrorMessage),
            o => o.ErrorMessage,
            (o, v) => o.ErrorMessage = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, string> CurrentFrameTextProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, string>(
            nameof(CurrentFrameText),
            o => o.CurrentFrameText,
            (o, v) => o.CurrentFrameText = v);

    public static readonly DirectProperty<ImageSequencePreviewControl, string> TotalFramesTextProperty =
        AvaloniaProperty.RegisterDirect<ImageSequencePreviewControl, string>(
            nameof(TotalFramesText),
            o => o.TotalFramesText,
            (o, v) => o.TotalFramesText = v);

    public string? FolderPath
    {
        get => _folderPath;
        set => SetAndRaise(FolderPathProperty, ref _folderPath, value);
    }

    public Bitmap? CurrentImage
    {
        get => _currentImage;
        set => SetAndRaise(CurrentImageProperty, ref _currentImage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetAndRaise(IsLoadingProperty, ref _isLoading, value);
    }

    public int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            if (SetAndRaise(CurrentFrameProperty, ref _currentFrame, value))
            {
                UpdateCurrentImageAsync().FireAndForget(
                    source: nameof(ImageSequencePreviewControl),
                    message: "图片序列当前帧后台刷新失败。");
                UpdateFrameTexts();
            }
        }
    }

    public int MaxFrame
    {
        get => _maxFrame;
        set
        {
            if (SetAndRaise(MaxFrameProperty, ref _maxFrame, value))
            {
                UpdateFrameTexts();
            }
        }
    }

    public bool HasImages
    {
        get => _hasImages;
        set => SetAndRaise(HasImagesProperty, ref _hasImages, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetAndRaise(HasErrorProperty, ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetAndRaise(ErrorMessageProperty, ref _errorMessage, value);
    }

    public string CurrentFrameText
    {
        get => $"ImageSequence_Frame:{_currentFrame + 1}";
        private set { } // 只读属性，不需要setter
    }

    public string TotalFramesText
    {
        get => $"ImageSequence_TotalFrames:{_imageFiles.Count}";
        private set { } // 只读属性，不需要setter
    }

    public ImageSequencePreviewControl()
    {
        InitializeComponent();

        // 监听文件夹路径变化
        this.GetObservable(FolderPathProperty).Subscribe(new Observer<string?>(OnFolderPathChanged));
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // 如果路径已经设置但没有触发变化事件，手动触发一次
        if (!string.IsNullOrEmpty(FolderPath))
        {
            OnFolderPathChanged(FolderPath);
        }
    }

    private void OnFolderPathChanged(string? newPath)
    {
        // 清除之前的错误状态
        HasError = false;
        ErrorMessage = string.Empty;

        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"OnFolderPathChanged: '{newPath}'", "ImageSequencePreviewControl");
        if (DataContext is RenderTaskViewModel task)
        {
            SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "ImageSequencePreview.FolderPathChanged",
                $"path={newPath ?? "<null>"}");
        }

        // 停止之前的文件监控
        StopFileWatcher();
        CancelPendingRefresh();

        if (string.IsNullOrEmpty(newPath))
        {
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, "Path is null or empty, clearing images", "ImageSequencePreviewControl");
            ClearImages();
            return;
        }

        // 如果路径是文件，获取其所在目录
        string directoryPath;
        if (File.Exists(newPath))
        {
            directoryPath = Path.GetDirectoryName(newPath) ?? string.Empty;
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Path is file, directory: '{directoryPath}'", "ImageSequencePreviewControl");
        }
        else if (Directory.Exists(newPath))
        {
            directoryPath = newPath;
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Path is directory: '{directoryPath}'", "ImageSequencePreviewControl");
        }
        else
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Path does not exist: '{newPath}'", "ImageSequencePreviewControl");
            SetError($"ImageSequence_PathNotExists:{newPath}");
            return;
        }

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Directory does not exist: '{directoryPath}'", "ImageSequencePreviewControl");
            SetError($"ImageSequence_PathNotExists:{directoryPath}");
            return;
        }

        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Loading image sequence from: '{directoryPath}'", "ImageSequencePreviewControl");
        LoadImageSequenceAsync(directoryPath).FireAndForget(
            source: nameof(ImageSequencePreviewControl),
            message: "图片序列后台加载失败。");

        // 启动文件监控
        StartFileWatcher(directoryPath);
    }

    private async Task LoadImageSequenceAsync(string folderPath)
    {
        var requestVersion = Interlocked.Increment(ref _loadRequestVersion);
        var previousCurrentFrame = _currentFrame;
        var loadStopwatch = Stopwatch.StartNew();

        await _sequenceLoadLock.WaitAsync();
        try
        {
            if (_disposed || requestVersion != _loadRequestVersion)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
                HasImages = false;
            });
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"LoadImageSequence: '{folderPath}'", "ImageSequencePreviewControl");

            var files = await Task.Run(() =>
            {
                var allFiles = Directory.GetFiles(folderPath);
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Found {allFiles.Length} total files in directory", "ImageSequencePreviewControl");

                return allFiles
                    .Where(file => ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .OrderBy(file => file)
                    .ToList();
            });

            if (_disposed || requestVersion != _loadRequestVersion)
            {
                return;
            }

            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Found {files.Count} image files", "ImageSequencePreviewControl");
            await Dispatcher.UIThread.InvokeAsync(() => ApplyImageSequence(files, previousCurrentFrame));

            if (DataContext is RenderTaskViewModel task)
            {
                SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "ImageSequencePreview.SequenceLoaded",
                    $"images={files.Count} loadElapsed={loadStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            }
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"加载图片序列失败: {ex.Message}", "ImageSequencePreviewControl");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetError($"ImageSequence_LoadFailed:{ex.Message}");
                ClearImages();
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            _sequenceLoadLock.Release();
        }
    }

    private Task UpdateCurrentImageAsync()
    {
        if (_imageFiles.Count == 0 || _currentFrame < 0 || _currentFrame >= _imageFiles.Count)
        {
            CurrentImage = null;
            return Task.CompletedTask;
        }

        // 如果图片已缓存，直接使用
        if (_imageCache[_currentFrame] != null)
        {
            CurrentImage = _imageCache[_currentFrame];
            return Task.CompletedTask;
        }

        return LoadImageAsync(_currentFrame, _loadRequestVersion);
    }

    private async Task LoadImageAsync(int frameIndex, int sequenceVersion)
    {
        if (frameIndex < 0 || frameIndex >= _imageFiles.Count)
            return;

        var filePath = _imageFiles[frameIndex];
        var imageLoadVersion = Interlocked.Increment(ref _imageLoadRequestVersion);
        var loadStopwatch = Stopwatch.StartNew();

        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() => new Bitmap(filePath));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed ||
                    sequenceVersion != _loadRequestVersion ||
                    imageLoadVersion != _imageLoadRequestVersion ||
                    frameIndex < 0 ||
                    frameIndex >= _imageFiles.Count ||
                    _imageCache.Length <= frameIndex ||
                    !string.Equals(_imageFiles[frameIndex], filePath, StringComparison.Ordinal))
                {
                    bitmap.Dispose();
                    return;
                }

                if (_imageCache[frameIndex] == null)
                {
                    _imageCache[frameIndex] = bitmap;
                }
                else
                {
                    bitmap.Dispose();
                }

                if (frameIndex == _currentFrame)
                {
                    CurrentImage = _imageCache[frameIndex];
                }
            });

            if (DataContext is RenderTaskViewModel task)
            {
                SelectionPerfTrace.Mark(task.Id, task.BlendFileName, "ImageSequencePreview.FrameLoaded",
                    $"frameIndex={frameIndex} loadElapsed={loadStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            }
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"加载图片失败 {filePath}: {ex.Message}", "ImageSequencePreviewControl");
        }
    }

    private void UpdateFrameTexts()
    {
        // 触发文本属性更新
        RaisePropertyChanged(CurrentFrameTextProperty!, null, CurrentFrameText);
        RaisePropertyChanged(TotalFramesTextProperty!, null, TotalFramesText);
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        HasImages = false;
        MaxFrame = 0;
        CurrentFrame = 0;
        CurrentImage = null;
    }

    private void ClearImages()
    {
        Interlocked.Increment(ref _imageLoadRequestVersion);
        DisposeImageCache();
        _imageFiles = [];
        HasImages = false;
        MaxFrame = 0;
        CurrentFrame = 0;
        CurrentImage = null;
        UpdateFrameTexts();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CancelPendingRefresh();
        Interlocked.Increment(ref _loadRequestVersion);
        Interlocked.Increment(ref _imageLoadRequestVersion);
        ClearImages();
        StopFileWatcher();
    }

    private void StartFileWatcher(string directoryPath)
    {
        try
        {
            StopFileWatcher(); // 确保之前的监控器已停止

            _fileWatcher = new FileSystemWatcher(directoryPath)
            {
                Filter = "*.*",
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };

            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Deleted += OnFileChanged;
            _fileWatcher.Renamed += OnFileRenamed;
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Error += OnFileWatcherError;

            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"File watcher started for: '{directoryPath}'", "ImageSequencePreviewControl");
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Failed to start file watcher: {ex.Message}", "ImageSequencePreviewControl");
        }
    }

    private void StopFileWatcher()
    {
        if (_fileWatcher == null) return;
        try
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Created -= OnFileChanged;
            _fileWatcher.Deleted -= OnFileChanged;
            _fileWatcher.Renamed -= OnFileRenamed;
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Error -= OnFileWatcherError;
            _fileWatcher.Dispose();
            _fileWatcher = null;
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, "File watcher stopped", "ImageSequencePreviewControl");
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error stopping file watcher: {ex.Message}", "ImageSequencePreviewControl");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();

        if (!ImageExtensions.Contains(extension))
            return;

        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"File {e.ChangeType}: {e.FullPath}", "ImageSequencePreviewControl");
        ScheduleRefresh();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var oldExtension = Path.GetExtension(e.OldFullPath).ToLowerInvariant();
        var newExtension = Path.GetExtension(e.FullPath).ToLowerInvariant();

        if (!ImageExtensions.Contains(oldExtension) && !ImageExtensions.Contains(newExtension))
            return;

        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"File renamed: {e.OldFullPath} -> {e.FullPath}", "ImageSequencePreviewControl");
        ScheduleRefresh();
    }

    private void OnFileWatcherError(object sender, ErrorEventArgs e)
    {
        ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"File watcher error: {e.GetException().Message}", "ImageSequencePreviewControl");
        // 可以在这里添加错误处理逻辑，比如重新启动监控器
    }

    private async Task RefreshImageSequenceAsync()
    {
        if (string.IsNullOrEmpty(_folderPath))
            return;

        try
        {
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, "Refreshing image sequence due to file changes", "ImageSequencePreviewControl");

            // 重新加载图片序列
            var directoryPath = File.Exists(_folderPath) ? Path.GetDirectoryName(_folderPath) : _folderPath;
            if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath))
            {
                await LoadImageSequenceAsync(directoryPath);
            }
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error refreshing image sequence: {ex.Message}", "ImageSequencePreviewControl");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelPendingRefresh();
        StopFileWatcher();
        ClearImages();
        _sequenceLoadLock.Dispose();
        _refreshDebounceCts?.Dispose();
    }

    private void PreviewImage_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_imageFiles.Count == 0 || _currentFrame < 0 || _currentFrame >= _imageFiles.Count)
            return;

        try
        {
            var currentImagePath = _imageFiles[_currentFrame];
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Opening image preview for: {currentImagePath}", "ImageSequencePreviewControl");

            // 先创建ViewModel
            var viewModel = new ImagePreviewWindowViewModel(currentImagePath, _currentFrame + 1);
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"ViewModel created, ImagePath: '{viewModel.ImagePath}'", "ImageSequencePreviewControl");

            // 使用接受ViewModel的构造函数创建窗口
            var previewWindow = new ImagePreviewWindow(viewModel);
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Window created with ViewModel", "ImageSequencePreviewControl");

            // 使用 ToplevelService 获取父窗口
            var parentTopLevel = ToplevelService.GetTopLevelForContext(this);
            if (parentTopLevel is Window parentWindow)
            {
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Showing as dialog", "ImageSequencePreviewControl");
                previewWindow.ShowDialog(parentWindow);
            }
            else
            {
                ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Showing as window", "ImageSequencePreviewControl");
                previewWindow.Show();
            }
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error opening image preview: {ex.Message}", "ImageSequencePreviewControl");
        }
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(FolderPath))
        {
            FileSystemHelper.OpenFileDirectory(FolderPath);
        }
    }

    private void ScheduleRefresh()
    {
        CancelPendingRefresh();
        var cts = new CancellationTokenSource();
        _refreshDebounceCts = cts;
        DebounceRefreshAsync(cts.Token).FireAndForget(
            source: nameof(ImageSequencePreviewControl),
            message: "图片序列防抖刷新后台任务失败。");
    }

    private async Task DebounceRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await RefreshImageSequenceAsync();
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    private void CancelPendingRefresh()
    {
        _refreshDebounceCts?.Cancel();
        _refreshDebounceCts?.Dispose();
        _refreshDebounceCts = null;
    }

    private void DisposeImageCache()
    {
        foreach (var bitmap in _imageCache)
        {
            bitmap?.Dispose();
        }

        _imageCache = [];
    }

    private void ApplyImageSequence(IReadOnlyList<string> files, int previousCurrentFrame)
    {
        DisposeImageCache();
        _imageFiles = files.ToList();

        if (_imageFiles.Count == 0)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, "No image files found", "ImageSequencePreviewControl");
            HasImages = false;
            MaxFrame = 0;
            CurrentFrame = 0;
            CurrentImage = null;
            UpdateFrameTexts();
            return;
        }

        HasImages = true;
        MaxFrame = _imageFiles.Count - 1;
        _imageCache = new Bitmap?[_imageFiles.Count];

        var targetFrame = (previousCurrentFrame >= 0 && previousCurrentFrame < _imageFiles.Count)
            ? previousCurrentFrame
            : 0;
        CurrentFrame = targetFrame;
        UpdateFrameTexts();

        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Successfully loaded {_imageFiles.Count} images, restored to frame {targetFrame}", "ImageSequencePreviewControl");
    }
}
