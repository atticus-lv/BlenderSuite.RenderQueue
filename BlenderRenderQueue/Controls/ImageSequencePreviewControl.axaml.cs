using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;

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
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}

public partial class ImageSequencePreviewControl : UserControl
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
        set 
        {
            Console.WriteLine($"[ImageSequencePreviewControl] FolderPath setter called with: '{value}'");
            SetAndRaise(FolderPathProperty, ref _folderPath, value);
        }
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
        get => $"帧: {_currentFrame + 1}";
        private set { } // 只读属性，不需要setter
    }

    public string TotalFramesText
    {
        get => $"共 {_imageFiles.Count} 帧";
        private set { } // 只读属性，不需要setter
    }

    public ImageSequencePreviewControl()
    {
        InitializeComponent();
        DataContext = this;
        
        System.Diagnostics.Debug.WriteLine("[ImageSequencePreviewControl] Constructor called");
        
        // 监听文件夹路径变化
        this.GetObservable(FolderPathProperty).Subscribe(new Observer<string?>(OnFolderPathChanged));
        
        System.Diagnostics.Debug.WriteLine("[ImageSequencePreviewControl] Constructor completed");
        
        // 测试初始值
        System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Initial FolderPath: '{FolderPath}'");
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        System.Diagnostics.Debug.WriteLine("[ImageSequencePreviewControl] OnLoaded called");
        System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Current FolderPath: '{FolderPath}'");
        
        // 如果路径已经设置但没有触发变化事件，手动触发一次
        if (!string.IsNullOrEmpty(FolderPath))
        {
            System.Diagnostics.Debug.WriteLine("[ImageSequencePreviewControl] Manually triggering OnFolderPathChanged");
            OnFolderPathChanged(FolderPath);
        }
    }

    private void OnFolderPathChanged(string? newPath)
    {
        // 清除之前的错误状态
        HasError = false;
        ErrorMessage = string.Empty;

        System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] OnFolderPathChanged: '{newPath}'");

        if (string.IsNullOrEmpty(newPath))
        {
            System.Diagnostics.Debug.WriteLine("[ImageSequencePreviewControl] Path is null or empty, clearing images");
            ClearImages();
            return;
        }

        // 如果路径是文件，获取其所在目录
        string directoryPath;
        if (File.Exists(newPath))
        {
            directoryPath = Path.GetDirectoryName(newPath) ?? string.Empty;
            System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Path is file, directory: '{directoryPath}'");
        }
        else if (Directory.Exists(newPath))
        {
            directoryPath = newPath;
            System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Path is directory: '{directoryPath}'");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Path does not exist: '{newPath}'");
            SetError($"路径不存在: {newPath}");
            return;
        }

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Directory does not exist: '{directoryPath}'");
            SetError($"文件夹不存在: {directoryPath}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Loading image sequence from: '{directoryPath}'");
        LoadImageSequence(directoryPath);
    }

    private async void LoadImageSequence(string folderPath)
    {
        IsLoading = true;
        HasImages = false;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] LoadImageSequence: '{folderPath}'");
            
            // 获取所有jpg和png文件，按文件名排序
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var allFiles = Directory.GetFiles(folderPath);
            System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Found {allFiles.Length} total files in directory");
            
            var files = allFiles
                .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .OrderBy(file => file)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Found {files.Count} image files");
            foreach (var file in files)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Image file: {Path.GetFileName(file)}");
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
                CurrentFrame = 0;
                
                // 初始化图片缓存
                _imageCache = new Bitmap?[_imageFiles.Count];
                
                System.Diagnostics.Debug.WriteLine($"[ImageSequencePreviewControl] Successfully loaded {_imageFiles.Count} images");
                
                // 预加载第一张图片
                await LoadImageAsync(0);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ImageSequencePreviewControl] No image files found");
                HasImages = false;
                MaxFrame = 0;
                CurrentFrame = 0;
                CurrentImage = null;
            }
        }
        catch (Exception ex)
        {
            // 处理加载错误
            System.Diagnostics.Debug.WriteLine($"加载图片序列失败: {ex.Message}");
            SetError($"加载图片序列失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"加载图片失败 {_imageFiles[frameIndex]}: {ex.Message}");
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
    }
}