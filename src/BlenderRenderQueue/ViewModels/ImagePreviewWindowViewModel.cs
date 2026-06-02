using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BlenderRenderQueue.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using BlenderRenderQueue.Services.Application.Logging;

namespace BlenderRenderQueue.ViewModels;

public partial class ImagePreviewWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private Bitmap? _image;

    partial void OnImageChanged(Bitmap? value)
    {
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Image property changed: {(value != null ? "Image set" : "Image cleared")}", "ImagePreviewWindowViewModel");
    }

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private string _imageSize = string.Empty;

    [ObservableProperty]
    private string _fileInfo = string.Empty;

    [ObservableProperty]
    private string _frameInfo = string.Empty;

    public ImagePreviewWindowViewModel()
    {
    }

    public ImagePreviewWindowViewModel(string imagePath, int frameNumber = 0)
    {
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Constructor called with path: '{imagePath}', frame: {frameNumber}", "ImagePreviewWindowViewModel");
        ImagePath = imagePath;
        FrameInfo = frameNumber > 0 ? $"帧 {frameNumber}" : "单帧";
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"ImagePath set to: '{ImagePath}'", "ImagePreviewWindowViewModel");
        LoadImageAsync().FireAndForget(
            source: nameof(ImagePreviewWindowViewModel),
            message: "图片预览窗口后台加载图片失败。");
    }

    private async Task LoadImageAsync()
    {
        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"LoadImageAsync called with path: '{ImagePath}'", "ImagePreviewWindowViewModel");
        
        if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Image path is invalid or file doesn't exist: '{ImagePath}'", "ImagePreviewWindowViewModel");
            return;
        }

        ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Starting to load image: '{ImagePath}'", "ImagePreviewWindowViewModel");
        IsLoading = true;
        
        try
        {
            // 在后台线程加载原图
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    using (var fileStream = File.OpenRead(ImagePath))
                    {
                        return new Bitmap(fileStream);
                    }
                }
                catch (Exception ex)
                {
                    ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error loading image: {ex.Message}", "ImagePreviewWindowViewModel");
                    return null;
                }
            });

            if (bitmap != null)
            {
                // 在UI线程更新属性
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Image = bitmap;
                    ImageSize = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}";
                    
                    // 获取文件信息
                    var fileInfo = new FileInfo(ImagePath);
                    FileInfo = $"{fileInfo.Length / 1024.0 / 1024.0:F1} MB • {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
                    
                    IsLoading = false;
                    ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"✅ Image loaded successfully: {ImagePath}", "ImagePreviewWindowViewModel");
                    ApplicationLogWriter.Write(RenderLogLevel.Info, RenderLogScope.System, $"Image size: {ImageSize}, File info: {FileInfo}", "ImagePreviewWindowViewModel");
                });
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsLoading = false;
                });
            }
        }
        catch (Exception ex)
        {
            ApplicationLogWriter.Write(RenderLogLevel.Error, RenderLogScope.System, $"Error in LoadImageAsync: {ex.Message}", "ImagePreviewWindowViewModel");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
            });
        }
    }

    public void Dispose()
    {
        Image?.Dispose();
        Image = null;
    }
}
