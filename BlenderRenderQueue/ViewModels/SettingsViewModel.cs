using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services.BlenderService;
using Avalonia.Platform.Storage;
using System.Threading;
using BlenderRenderQueue.Services;

namespace BlenderRenderQueue.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _blenderPath = string.Empty;

    [ObservableProperty]
    private string _ffmpegPath = string.Empty;

    [ObservableProperty]
    private bool _isBlenderPathValid = false;

    [ObservableProperty]
    private bool _isFFmpegPathValid = false;

    [ObservableProperty]
    private string _blenderVersion = string.Empty;

    [ObservableProperty]
    private string _blenderPlatform = string.Empty;

    [ObservableProperty]
    private string _blenderBranch = string.Empty;

    [ObservableProperty]
    private string _blenderHash = string.Empty;

    [ObservableProperty]
    private string _ffmpegVersion = string.Empty;

    [ObservableProperty]
    private bool _isLoadingBlenderInfo = false;

    [ObservableProperty]
    private bool _isLoadingFFmpegInfo = false;

    // 内部状态
    private CancellationTokenSource? _versionCts;

    // 事件：当设置发生变化时通知
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public SettingsViewModel()
    {
        // Windows 上尝试自动定位 Blender 和 FFmpeg
        TryAutoDetectBlender();
        TryAutoDetectFFmpeg();
    }

    partial void OnBlenderPathChanged(string value)
    {
        _versionCts?.Cancel();
        _versionCts = new CancellationTokenSource();
        var ct = _versionCts.Token;

        IsBlenderPathValid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);

        if (!IsBlenderPathValid)
        {
            ClearBlenderInfo();
            return;
        }

        // 异步获取Blender版本信息
        _ = Task.Run(async () => await LoadBlenderInfoAsync(value, ct));
    }

    partial void OnFfmpegPathChanged(string value)
    {
        IsFFmpegPathValid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);

        if (!IsFFmpegPathValid)
        {
            FfmpegVersion = string.Empty;
            return;
        }

        // 异步获取FFmpeg版本信息
        _ = Task.Run(async () => await LoadFFmpegInfoAsync(value));
    }

    private async Task LoadBlenderInfoAsync(string blenderPath, CancellationToken cancellationToken)
    {
        try
        {
            IsLoadingBlenderInfo = true;

            var svc = new BlenderCliInfoService();
            var info = await svc.GetVersionInfoAsync(blenderPath, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            // 更新UI线程上的属性
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                BlenderVersion = info.Version;
                BlenderPlatform = info.Platform;
                BlenderBranch = info.Branch;
                BlenderHash = info.Hash;
                IsLoadingBlenderInfo = false;
            });
        }
        catch (Exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsLoadingBlenderInfo = false;
                    ClearBlenderInfo();
                });
            }
        }
    }

    private async Task LoadFFmpegInfoAsync(string ffmpegPath)
    {
        try
        {
            IsLoadingFFmpegInfo = true;

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                // 解析版本信息
                var lines = output.Split('\n');
                var versionLine = lines.FirstOrDefault(l => l.Contains("ffmpeg version"));
                if (!string.IsNullOrEmpty(versionLine))
                {
                    var version = versionLine.Split(' ')[2]; // 提取版本号
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        FfmpegVersion = version;
                        IsLoadingFFmpegInfo = false;
                    });
                }
            }
        }
        catch (Exception)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsLoadingFFmpegInfo = false;
                FfmpegVersion = string.Empty;
            });
        }
    }

    private void ClearBlenderInfo()
    {
        BlenderVersion = string.Empty;
        BlenderPlatform = string.Empty;
        BlenderBranch = string.Empty;
        BlenderHash = string.Empty;
    }

    private void TryAutoDetectBlender()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (BlenderRenderQueue.Helpers.BlenderLocator.TryFindBlenderExe(out var exe))
                {
                    BlenderPath = exe;
                    return;
                }

                // 未命中则后台异步扫描常见目录
                _ = Task.Run(async () =>
                {
                    var asyncExe = await BlenderRenderQueue.Helpers.BlenderLocator.FindBlenderExeAsync();
                    if (!string.IsNullOrWhiteSpace(asyncExe))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            BlenderPath = asyncExe;
                        });
                    }
                });
            }
        }
        catch
        {
            // 忽略错误
        }
    }

    private void TryAutoDetectFFmpeg()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // 尝试在 PATH 中查找 ffmpeg.exe
                var ffmpegExe = FindFFmpegInPath();
                if (!string.IsNullOrEmpty(ffmpegExe))
                {
                    FfmpegPath = ffmpegExe;
                    return;
                }

                // 尝试在常见位置查找
                var commonPaths = new[]
                {
                    @"C:\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                    @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ffmpeg", "bin", "ffmpeg.exe")
                };

                foreach (var path in commonPaths)
                {
                    if (!File.Exists(path)) continue;
                    FfmpegPath = path;
                    return;
                }
            }
        }
        catch
        {
            // 忽略错误
        }
    }

    private string? FindFFmpegInPath()
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                var ffmpegPath = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(ffmpegPath))
                {
                    return ffmpegPath;
                }
            }
        }
        catch
        {
            // 忽略错误
        }
        return null;
    }

    [RelayCommand]
    private async Task BrowseBlender()
    {
        var path = await this.SelectFile("选择 Blender 可执行文件", GetBlenderExecutableFileTypes());
        if (!string.IsNullOrWhiteSpace(path))
        {
            BlenderPath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseFFmpeg()
    {
        var path = await this.SelectFile("选择 FFmpeg 可执行文件", GetFFmpegExecutableFileTypes());
        if (!string.IsNullOrWhiteSpace(path))
        {
            FfmpegPath = path;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // 触发设置变化事件
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(BlenderPath, FfmpegPath));
    }

    private static IEnumerable<FilePickerFileType> GetBlenderExecutableFileTypes()
    {
#if WINDOWS
        return new[] { new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } } };
#else
        return new[] { new FilePickerFileType("Blender") { Patterns = new[] { "blender", "*blender*" } } };
#endif
    }

    private static IEnumerable<FilePickerFileType> GetFFmpegExecutableFileTypes()
    {
#if WINDOWS
        return new[] { new FilePickerFileType("FFmpeg Executable") { Patterns = new[] { "ffmpeg.exe" } } };
#else
        return new[] { new FilePickerFileType("FFmpeg") { Patterns = new[] { "ffmpeg", "*ffmpeg*" } } };
#endif
    }

    public void Dispose()
    {
        _versionCts?.Cancel();
        _versionCts?.Dispose();
    }
}

// 设置变化事件参数
public class SettingsChangedEventArgs : EventArgs
{
    public string BlenderPath { get; }
    public string FfmpegPath { get; }

    public SettingsChangedEventArgs(string blenderPath, string ffmpegPath)
    {
        BlenderPath = blenderPath;
        FfmpegPath = ffmpegPath;
    }
}
