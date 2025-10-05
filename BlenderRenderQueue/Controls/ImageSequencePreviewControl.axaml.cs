using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;
using BlenderRenderQueue.Views;
using BlenderRenderQueue.ViewModels;
using BlenderRenderQueue.Helpers;
using BlenderRenderQueue.Services;

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
    { }

    public void OnCompleted()
    { }
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
    private ObservableCollection<string> _imageFiles = new();
    private Bitmap?[] _imageCache;
    private FileSystemWatcher? _fileWatcher;
    private readonly object _lockObject = new object();

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
                UpdateCurrentImage();
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

        Console.WriteLine($"[ImageSequencePreviewControl] OnFolderPathChanged: '{newPath}'");

        // 停止之前的文件监控
        StopFileWatcher();

        if (string.IsNullOrEmpty(newPath))
        {
            Console.WriteLine("[ImageSequencePreviewControl] Path is null or empty, clearing images");
            ClearImages();
            return;
        }

        // 如果路径是文件，获取其所在目录
        string directoryPath;
        if (File.Exists(newPath))
        {
            directoryPath = Path.GetDirectoryName(newPath) ?? string.Empty;
            Console.WriteLine($"[ImageSequencePreviewControl] Path is file, directory: '{directoryPath}'");
        }
        else if (Directory.Exists(newPath))
        {
            directoryPath = newPath;
            Console.WriteLine($"[ImageSequencePreviewControl] Path is directory: '{directoryPath}'");
        }
        else
        {
            Console.WriteLine($"[ImageSequencePreviewControl] Path does not exist: '{newPath}'");
            SetError($"ImageSequence_PathNotExists:{newPath}");
            return;
        }

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            Console.WriteLine($"[ImageSequencePreviewControl] Directory does not exist: '{directoryPath}'");
            SetError($"ImageSequence_PathNotExists:{directoryPath}");
            return;
        }

        Console.WriteLine($"[ImageSequencePreviewControl] Loading image sequence from: '{directoryPath}'");
        LoadImageSequence(directoryPath);

        // 启动文件监控
        StartFileWatcher(directoryPath);
    }

    private async void LoadImageSequence(string folderPath)
    {
        IsLoading = true;
        HasImages = false;

        // 保存当前帧位置，用于在重新加载后恢复
        var previousCurrentFrame = _currentFrame;

        try
        {
            Console.WriteLine($"[ImageSequencePreviewControl] LoadImageSequence: '{folderPath}'");

            // 获取所有jpg和png文件，按文件名排序
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var allFiles = Directory.GetFiles(folderPath);
            Console.WriteLine($"[ImageSequencePreviewControl] Found {allFiles.Length} total files in directory");

            var files = allFiles
                .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .OrderBy(file => file)
                .ToList();

            Console.WriteLine($"[ImageSequencePreviewControl] Found {files.Count} image files");
            foreach (var file in files)
            {
                Console.WriteLine($"[ImageSequencePreviewControl] Image file: {Path.GetFileName(file)}");
            }

            _imageFiles.Clear();
            foreach (var file in files)
            {
                _imageFiles.Add(file);
            }

            if (_imageFiles.Count > 0)
            {
                HasImages = true;
                MaxFrame = _imageFiles.Count - 1;
                
                // 尝试恢复到之前的帧位置，如果无效则使用0
                var targetFrame = (previousCurrentFrame >= 0 && previousCurrentFrame < _imageFiles.Count) 
                    ? previousCurrentFrame 
                    : 0;
                CurrentFrame = targetFrame;

                // 初始化图片缓存
                _imageCache = new Bitmap?[_imageFiles.Count];

                Console.WriteLine($"[ImageSequencePreviewControl] Successfully loaded {_imageFiles.Count} images, restored to frame {targetFrame}");

                // 预加载目标帧的图片
                await LoadImageAsync(targetFrame);
            }
            else
            {
                Console.WriteLine("[ImageSequencePreviewControl] No image files found");
                HasImages = false;
                MaxFrame = 0;
                CurrentFrame = 0;
                CurrentImage = null;
            }
        }
        catch (Exception ex)
        {
            // 处理加载错误
            Console.WriteLine($"加载图片序列失败: {ex.Message}");
            SetError($"ImageSequence_LoadFailed:{ex.Message}");
            ClearImages();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void UpdateCurrentImage()
    {
        if (_imageFiles.Count == 0 || _currentFrame < 0 || _currentFrame >= _imageFiles.Count)
        {
            CurrentImage = null;
            return;
        }

        // 如果图片已缓存，直接使用
        if (_imageCache[_currentFrame] != null)
        {
            CurrentImage = _imageCache[_currentFrame];
            return;
        }

        // 异步加载图片
        await LoadImageAsync(_currentFrame);
    }

    private async Task LoadImageAsync(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _imageFiles.Count)
            return;

        try
        {
            var filePath = _imageFiles[frameIndex];
            var bitmap = new Bitmap(filePath);
            _imageCache[frameIndex] = bitmap;

            // 如果是当前帧，更新显示
            if (frameIndex == _currentFrame)
            {
                CurrentImage = bitmap;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载图片失败 {_imageFiles[frameIndex]}: {ex.Message}");
        }
    }

    private void UpdateFrameTexts()
    {
        // 触发文本属性更新
        RaisePropertyChanged(CurrentFrameTextProperty, null, CurrentFrameText);
        RaisePropertyChanged(TotalFramesTextProperty, null, TotalFramesText);
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
        // 释放缓存的图片
        if (_imageCache != null)
        {
            foreach (var bitmap in _imageCache)
            {
                bitmap?.Dispose();
            }

            _imageCache = null;
        }

        _imageFiles.Clear();
        HasImages = false;
        MaxFrame = 0;
        CurrentFrame = 0;
        CurrentImage = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
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

            Console.WriteLine($"[ImageSequencePreviewControl] File watcher started for: '{directoryPath}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageSequencePreviewControl] Failed to start file watcher: {ex.Message}");
        }
    }

    private void StopFileWatcher()
    {
        if (_fileWatcher != null)
        {
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
                Console.WriteLine("[ImageSequencePreviewControl] File watcher stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImageSequencePreviewControl] Error stopping file watcher: {ex.Message}");
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // 检查是否是图片文件
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png" };
        var extension = Path.GetExtension(e.FullPath).ToLowerInvariant();

        if (!imageExtensions.Contains(extension))
            return;

        Console.WriteLine($"[ImageSequencePreviewControl] File {e.ChangeType}: {e.FullPath}");

        // 使用防抖动机制，避免频繁更新
        _ = Task.Run(async () =>
        {
            await Task.Delay(500); // 等待500ms，避免文件正在被写入时读取
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    RefreshImageSequence();
                }
            });
        });
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // 检查是否是图片文件
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png" };
        var oldExtension = Path.GetExtension(e.OldFullPath).ToLowerInvariant();
        var newExtension = Path.GetExtension(e.FullPath).ToLowerInvariant();

        if (!imageExtensions.Contains(oldExtension) && !imageExtensions.Contains(newExtension))
            return;

        Console.WriteLine($"[ImageSequencePreviewControl] File renamed: {e.OldFullPath} -> {e.FullPath}");

        // 使用防抖动机制
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    RefreshImageSequence();
                }
            });
        });
    }

    private void OnFileWatcherError(object sender, ErrorEventArgs e)
    {
        Console.WriteLine($"[ImageSequencePreviewControl] File watcher error: {e.GetException().Message}");
        // 可以在这里添加错误处理逻辑，比如重新启动监控器
    }

    private void RefreshImageSequence()
    {
        if (string.IsNullOrEmpty(_folderPath))
            return;

        try
        {
            Console.WriteLine("[ImageSequencePreviewControl] Refreshing image sequence due to file changes");

            // 重新加载图片序列
            var directoryPath = File.Exists(_folderPath) ? Path.GetDirectoryName(_folderPath) : _folderPath;
            if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath))
            {
                LoadImageSequence(directoryPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageSequencePreviewControl] Error refreshing image sequence: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopFileWatcher();
        ClearImages();
    }

    private void PreviewImage_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_imageFiles.Count == 0 || _currentFrame < 0 || _currentFrame >= _imageFiles.Count)
            return;

        try
        {
            var currentImagePath = _imageFiles[_currentFrame];
            Console.WriteLine($"[ImageSequencePreviewControl] Opening image preview for: {currentImagePath}");

            // 先创建ViewModel
            var viewModel = new ImagePreviewWindowViewModel(currentImagePath, _currentFrame + 1);
            Console.WriteLine($"[ImageSequencePreviewControl] ViewModel created, ImagePath: '{viewModel.ImagePath}'");

            // 使用接受ViewModel的构造函数创建窗口
            var previewWindow = new ImagePreviewWindow(viewModel);
            Console.WriteLine($"[ImageSequencePreviewControl] Window created with ViewModel");

            // 使用 ToplevelService 获取父窗口
            var parentTopLevel = ToplevelService.GetTopLevelForContext(this);
            if (parentTopLevel is Window parentWindow)
            {
                Console.WriteLine($"[ImageSequencePreviewControl] Showing as dialog");
                previewWindow.ShowDialog(parentWindow);
            }
            else
            {
                Console.WriteLine($"[ImageSequencePreviewControl] Showing as window");
                previewWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageSequencePreviewControl] Error opening image preview: {ex.Message}");
        }
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var success = FileSystemHelper.OpenFileDirectory(FolderPath);
    }
}