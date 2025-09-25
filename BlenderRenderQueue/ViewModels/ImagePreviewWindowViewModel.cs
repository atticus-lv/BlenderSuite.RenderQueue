using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using BlenderRenderQueue.Models;

namespace BlenderRenderQueue.ViewModels;

public partial class ImagePreviewWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private Bitmap? _image;

    partial void OnImageChanged(Bitmap? value)
    {
        Console.WriteLine($"[ImagePreviewWindowViewModel] Image property changed: {(value != null ? "Image set" : "Image cleared")}");
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
        Console.WriteLine($"[ImagePreviewWindowViewModel] Constructor called with path: '{imagePath}', frame: {frameNumber}");
        ImagePath = imagePath;
        FrameInfo = frameNumber > 0 ? $"帧 {frameNumber}" : "单帧";
        Console.WriteLine($"[ImagePreviewWindowViewModel] ImagePath set to: '{ImagePath}'");
        LoadImageAsync();
    }

    private async void LoadImageAsync()
    {
        Console.WriteLine($"[ImagePreviewWindowViewModel] LoadImageAsync called with path: '{ImagePath}'");
        
        if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
        {
            Console.WriteLine($"[ImagePreviewWindowViewModel] Image path is invalid or file doesn't exist: '{ImagePath}'");
            return;
        }

        Console.WriteLine($"[ImagePreviewWindowViewModel] Starting to load image: '{ImagePath}'");
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
                    Console.WriteLine($"[ImagePreviewWindowViewModel] Error loading image: {ex.Message}");
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
                    Console.WriteLine($"[ImagePreviewWindowViewModel] ✅ Image loaded successfully: {ImagePath}");
                    Console.WriteLine($"[ImagePreviewWindowViewModel] Image size: {ImageSize}, File info: {FileInfo}");
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
            Console.WriteLine($"[ImagePreviewWindowViewModel] Error in LoadImageAsync: {ex.Message}");
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
