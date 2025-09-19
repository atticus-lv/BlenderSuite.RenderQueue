using System;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlenderRenderQueue.Services;
using BlenderRenderQueue.Services.BlenderService;
using BlenderRenderQueue.Services.BlenderService.ServiceOutputParser;
using System.Collections.Concurrent;
using System.Threading;
using BlenderRenderQueue.ViewModels;
using Avalonia.Data.Converters;
using System.Globalization;
using BlenderRenderQueue.Models;
using Avalonia.Media.Imaging;
using System.IO;

namespace BlenderRenderQueue.ViewModels;

public partial class RenderTaskViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _blendFilePath = string.Empty;

    [ObservableProperty]
    private int _startFrame = 1;

    [ObservableProperty]
    private int _endFrame = 1;

    [ObservableProperty]
    private bool _animation = true;

    [ObservableProperty]
    private double _progress01; // 当前帧进度

    [ObservableProperty]
    private double _overallProgress01; // 整体进度

    [ObservableProperty]
    private string _engine = string.Empty;

    [ObservableProperty]
    private int _currentFrame;

    [ObservableProperty]
    private int _completedFrames;

    [ObservableProperty]
    private string _sampleText = string.Empty;

    [ObservableProperty]
    private string _savedPath = string.Empty;

    [ObservableProperty]
    private string _outputLog = string.Empty;

    [ObservableProperty]
    private BlendFilePropertiesViewModel _fileProperties = new();

    [ObservableProperty]
    private BlendFileInfo _fileInfo = new();

    [ObservableProperty]
    private Bitmap? _renderedImage;

    [ObservableProperty]
    private string _renderedImagePath = string.Empty;

    [ObservableProperty]
    private bool _hasRenderedImage = false;

    public string BlendFileName => System.IO.Path.GetFileName(BlendFilePath);

    [ObservableProperty]
    private bool _isLogPaused = false;

    [ObservableProperty]
    private string _logPauseButtonText = "暂停日志";

    [ObservableProperty]
    private int _renderTimeoutSeconds = 300; // 默认5分钟无活动超时

    [ObservableProperty]
    private RenderTaskStatus _status = RenderTaskStatus.Pending;

    [ObservableProperty]
    private string _statusText = "等待中";

    [ObservableProperty]
    private DateTime? _startTime;

    [ObservableProperty]
    private DateTime? _endTime;

    [ObservableProperty]
    private TimeSpan? _duration;

    [ObservableProperty]
    private BlendFilePropertiesViewModel _filePropertiesViewModel = new();

    // 保存停止时的进度状态
    private int _lastCompletedFrame = 0;
    private bool _wasStopped = false;

    // 内部状态
    private IRenderSession? _session;
    private BlenderExeService? _exe;
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly System.Timers.Timer _logTimer;
    private const int MaxLogLines = 1000;
    private int _logLineCount = 0;
    private volatile bool _isFlushing = false;
    private readonly object _logLock = new object();
    private DateTime _lastFlushTime = DateTime.MinValue;
    private const int MinFlushIntervalMs = 50;
    private const int MaxBatchSize = 100;

    // 事件
    public event EventHandler<RenderTaskStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<RenderTaskProgressEventArgs>? ProgressChanged;

    public RenderTaskViewModel()
    {
        // 日志批量刷新
        _logTimer = new System.Timers.Timer(200);
        _logTimer.Elapsed += (_, __) => FlushLogQueue();
        _logTimer.AutoReset = true;
        _logTimer.Start();
    }

    public RenderTaskViewModel(string blendFilePath, int startFrame, int endFrame, bool animation = true) : this()
    {
        BlendFilePath = blendFilePath;
        StartFrame = startFrame;
        EndFrame = endFrame;
        Animation = animation;
        
        // 加载文件信息
        LoadFileInfo();
    }

    private void LoadFileInfo()
    {
        if (!string.IsNullOrEmpty(BlendFilePath))
        {
            FileInfo = BlendFileInfo.FromFilePath(BlendFilePath);
        }
    }


    /// <summary>
    /// 加载并优化渲染图片
    /// </summary>
    private async Task LoadRenderedImageAsync(string imagePath)
    {
        try
        {
            Console.WriteLine($"[RenderTaskViewModel] Loading rendered image: {imagePath}");
            
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"[RenderTaskViewModel] Rendered image file does not exist: {imagePath}");
                return;
            }

            // 在后台线程加载图片
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    // 使用文件流加载图片，更安全
                    using (var fileStream = File.OpenRead(imagePath))
                    {
                        return new Bitmap(fileStream);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderTaskViewModel] Error loading image: {ex.Message}");
                    Console.WriteLine($"[RenderTaskViewModel] Stack trace: {ex.StackTrace}");
                    return null;
                }
            });

            if (bitmap != null)
            {
                Console.WriteLine($"[RenderTaskViewModel] Original image size: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
                
                // 在UI线程更新属性
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        RenderedImage?.Dispose();
                        RenderedImage = bitmap;
                        RenderedImagePath = imagePath;
                        HasRenderedImage = true;
                        Console.WriteLine($"[RenderTaskViewModel] ✅ Rendered image loaded successfully: {imagePath}");
                        Console.WriteLine($"[RenderTaskViewModel] Image size: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RenderTaskViewModel] Error setting rendered image: {ex.Message}");
                        HasRenderedImage = false;
                    }
                });
                
                // 在后台优化图片尺寸
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 重新从文件加载图片进行优化，避免使用可能被UI使用的bitmap
                        var optimizedBitmap = await LoadAndOptimizeImageAsync(imagePath, 120, 90);
                        
                        // 在UI线程更新为优化后的图片
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (optimizedBitmap != null)
                                {
                                    RenderedImage?.Dispose();
                                    RenderedImage = optimizedBitmap;
                                    Console.WriteLine($"[RenderTaskViewModel] ✅ Optimized image applied: {optimizedBitmap.PixelSize.Width}x{optimizedBitmap.PixelSize.Height}");
                                }
                                else
                                {
                                    Console.WriteLine($"[RenderTaskViewModel] ⚠️ Optimized bitmap is null, keeping original");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[RenderTaskViewModel] Error applying optimized image: {ex.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RenderTaskViewModel] Error optimizing image: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderTaskViewModel] Error in LoadRenderedImageAsync: {ex.Message}");
        }
    }

    /// <summary>
    /// 从文件加载并优化图片尺寸
    /// </summary>
    private async Task<Bitmap?> LoadAndOptimizeImageAsync(string imagePath, int maxWidth, int maxHeight)
    {
        return await Task.Run(() =>
        {
            try
            {
                Console.WriteLine($"[RenderTaskViewModel] Loading and optimizing image: {imagePath}");
                
                // 重新从文件加载图片
                using (var fileStream = File.OpenRead(imagePath))
                {
                    var originalBitmap = new Bitmap(fileStream);
                    var originalSize = originalBitmap.PixelSize;
                    
                    // 如果图片已经小于目标尺寸，直接返回
                    if (originalSize.Width <= maxWidth && originalSize.Height <= maxHeight)
                    {
                        Console.WriteLine($"[RenderTaskViewModel] Image already optimal size: {originalSize.Width}x{originalSize.Height}");
                        return originalBitmap;
                    }

                    // 计算缩放比例
                    var scaleX = (double)maxWidth / originalSize.Width;
                    var scaleY = (double)maxHeight / originalSize.Height;
                    var scale = Math.Min(scaleX, scaleY);

                    var newWidth = (int)(originalSize.Width * scale);
                    var newHeight = (int)(originalSize.Height * scale);

                    Console.WriteLine($"[RenderTaskViewModel] Optimizing image from {originalSize.Width}x{originalSize.Height} to {newWidth}x{newHeight}");

                    // 使用RenderTargetBitmap进行缩放
                    var renderTarget = new RenderTargetBitmap(new Avalonia.PixelSize(newWidth, newHeight));
                    using (var drawingContext = renderTarget.CreateDrawingContext())
                    {
                        var sourceRect = new Avalonia.Rect(0, 0, originalSize.Width, originalSize.Height);
                        var destRect = new Avalonia.Rect(0, 0, newWidth, newHeight);
                        
                        drawingContext.DrawImage(originalBitmap, sourceRect, destRect);
                    }

                    // 释放原始位图
                    originalBitmap.Dispose();
                    
                    Console.WriteLine($"[RenderTaskViewModel] Image optimization completed successfully");
                    return renderTarget;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderTaskViewModel] Error loading and optimizing image: {ex.Message}");
                Console.WriteLine($"[RenderTaskViewModel] Stack trace: {ex.StackTrace}");
                return null;
            }
        });
    }

    /// <summary>
    /// 优化图片尺寸（已弃用，使用LoadAndOptimizeImageAsync代替）
    /// </summary>
    private async Task<Bitmap> OptimizeImageSizeAsync(Bitmap originalBitmap, int maxWidth, int maxHeight)
    {
        return await Task.Run(() =>
        {
            try
            {
                var originalSize = originalBitmap.PixelSize;
                
                // 如果图片已经小于目标尺寸，直接返回
                if (originalSize.Width <= maxWidth && originalSize.Height <= maxHeight)
                {
                    Console.WriteLine($"[RenderTaskViewModel] Image already optimal size: {originalSize.Width}x{originalSize.Height}");
                    return originalBitmap;
                }

                // 计算缩放比例
                var scaleX = (double)maxWidth / originalSize.Width;
                var scaleY = (double)maxHeight / originalSize.Height;
                var scale = Math.Min(scaleX, scaleY);

                var newWidth = (int)(originalSize.Width * scale);
                var newHeight = (int)(originalSize.Height * scale);

                Console.WriteLine($"[RenderTaskViewModel] Optimizing image from {originalSize.Width}x{originalSize.Height} to {newWidth}x{newHeight}");

                // 使用RenderTargetBitmap进行缩放
                var renderTarget = new RenderTargetBitmap(new Avalonia.PixelSize(newWidth, newHeight));
                using (var drawingContext = renderTarget.CreateDrawingContext())
                {
                    var sourceRect = new Avalonia.Rect(0, 0, originalSize.Width, originalSize.Height);
                    var destRect = new Avalonia.Rect(0, 0, newWidth, newHeight);
                    
                    drawingContext.DrawImage(originalBitmap, sourceRect, destRect);
                }

                // 注意：不释放originalBitmap，因为它可能还在被UI使用
                Console.WriteLine($"[RenderTaskViewModel] Image optimization completed successfully");
                return renderTarget;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderTaskViewModel] Error optimizing image: {ex.Message}");
                Console.WriteLine($"[RenderTaskViewModel] Stack trace: {ex.StackTrace}");
                return originalBitmap; // 返回原始图片
            }
        });
    }

    public async Task LoadFilePropertiesAsync(BlenderExeService exeService)
    {
        if (string.IsNullOrWhiteSpace(BlendFilePath) || !System.IO.File.Exists(BlendFilePath))
        {
            EnqueueLog("文件路径无效或文件不存在");
            return;
        }

        try
        {
            EnqueueLog("[QUERY] 开始加载文件属性...");
            await FileProperties.LoadPropertiesAsync(exeService, BlendFilePath);

            // 从FileProperties获取帧范围信息
            StartFrame = FileProperties.SceneProperties.FrameStart;
            EndFrame = FileProperties.SceneProperties.FrameEnd;
            EnqueueLog($"[QUERY] 文件属性加载完成: 帧范围 {StartFrame}..{EndFrame}");
        }
        catch (Exception ex)
        {
            EnqueueLog($"[QUERY] 加载文件属性失败: {ex.Message}");
        }
    }

    public async Task StartRenderAsync(BlenderExeService exeService)
    {
        if (string.IsNullOrWhiteSpace(BlendFilePath))
        {
            EnqueueLog("请先选择 .blend 文件");
            return;
        }

        try
        {
            SetStatus(RenderTaskStatus.Running, "正在启动渲染...");
            StartTime = DateTime.Now;

            _exe = exeService;
            _exe.OnOutputReceived += HandleRawOutput;
            _exe.OnErrorReceived += HandleRawError;

            _session = new RenderSession(_exe, new RenderOutputParser());
            _session.OnProgress += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProgress(s));
            _session.OnEvent += e => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnEvent(e));

            var cmd = new BlenderCommandService();

            // 为渲染任务设置可配置的超时时间
            _exe.Timeout = RenderTimeoutSeconds;

            EnqueueLog($"开始渲染: {StartFrame}..{EndFrame}, animation={Animation} (无活动超时: {RenderTimeoutSeconds}秒)");

            await cmd.StartRenderAsync(_exe, BlendFilePath, StartFrame, EndFrame, Animation);
            EnqueueLog($"渲染指令已发送完成");
        }
        catch (TaskCanceledException ex)
        {
            if (ex.CancellationToken.IsCancellationRequested)
            {
                EnqueueLog("渲染任务被用户取消");
                SetStatus(RenderTaskStatus.Cancelled, "已取消");
            }
            else
            {
                EnqueueLog($"渲染任务超时: {ex.Message}");
                SetStatus(RenderTaskStatus.Failed, "超时");
            }
        }
        catch (OperationCanceledException ex)
        {
            EnqueueLog($"渲染操作被取消: {ex.Message}");
            SetStatus(RenderTaskStatus.Cancelled, "已取消");
        }
        catch (Exception ex)
        {
            EnqueueLog($"渲染启动失败: {ex.Message}");
            SetStatus(RenderTaskStatus.Failed, "启动失败");
        }
    }

    public void StopRender()
    {
        // 只停止渲染会话，不释放BlenderExeService
        try
        {
            _session?.Dispose();
        }
        catch
        { }

        _session = null;

        // 取消事件订阅，但不释放_exe服务
        if (_exe is not null)
        {
            _exe.OnOutputReceived -= HandleRawOutput;
            _exe.OnErrorReceived -= HandleRawError;
            // 注意：不释放_exe，因为它可能被其他任务使用
        }

        SetStatus(RenderTaskStatus.Cancelled, "已停止");
        EndTime = DateTime.Now;
        if (StartTime.HasValue)
        {
            Duration = EndTime.Value - StartTime.Value;
        }

        EnqueueLog("渲染已停止");
    }

    [RelayCommand]
    private void ClearLog()
    {
        OutputLog = string.Empty;
        _logLineCount = 0;
        // 清空队列中的待处理日志
        while (_logQueue.TryDequeue(out _))
        { }

        EnqueueLog("日志已清空");
    }

    [RelayCommand]
    private void ToggleLogPause()
    {
        IsLogPaused = !IsLogPaused;
        LogPauseButtonText = IsLogPaused ? "继续日志" : "暂停日志";
    }

    private void HandleRawOutput(string line)
    {
        EnqueueLog($"[OUT] {line}");
    }

    private void HandleRawError(string line)
    {
        EnqueueLog($"[ERR] {line}");
    }

    private void OnProgress(RenderProgress p)
    {
        Engine = p.Engine.ToString();
        CurrentFrame = p.CurrentFrame;
        SampleText = p.SampleCurrent.HasValue && p.SampleTotal.HasValue
            ? $"{p.SampleCurrent}/{p.SampleTotal}"
            : string.Empty;
        SavedPath = p.SavedPath ?? string.Empty;

        if (p.SampleCurrent.HasValue && p.SampleTotal.HasValue && p.SampleTotal.Value > 0)
        {
            Progress01 = Math.Clamp((double)p.SampleCurrent.Value / p.SampleTotal.Value, 0, 1);
        }
        else
        {
            Progress01 = 0;
        }

        // 计算整体进度（基于帧范围）
        var totalFrames = Math.Max(0, EndFrame - StartFrame + 1);
        if (totalFrames > 0)
        {
            CompletedFrames = Math.Max(0, p.CurrentFrame - StartFrame + 1);
            double perFrame = Progress01; // 当前帧内进度
            OverallProgress01 = Math.Clamp((CompletedFrames + perFrame) / totalFrames, 0, 1);
        }
        else
        {
            OverallProgress01 = 0;
        }

        // 触发进度变化事件
        ProgressChanged?.Invoke(this, new RenderTaskProgressEventArgs(OverallProgress01, Progress01, p.CurrentFrame));
    }

    private void OnEvent(RenderEvent e)
    {
        switch (e)
        {
            case RenderSessionStarted s:
                EnqueueLog(s.IsAnimation ? $"开始动画渲染: {s.StartFrame}..{s.EndFrame}" : $"开始单帧渲染");
                SetStatus(RenderTaskStatus.Running, "渲染中");
                break;
            case RenderStarted rs:
                EnqueueLog($"开始帧 {rs.Frame} ({rs.Engine}) {rs.Scene},{rs.ViewLayer}");
                break;
            case RenderSaved saved:
                EnqueueLog($"已保存: {saved.Path} (帧 {saved.Frame})");
                // 加载渲染完成的图片
                _ = Task.Run(async () => await LoadRenderedImageAsync(saved.Path));
                break;
            case RenderCompletedFrame done:
                EnqueueLog($"帧 {done.Frame} 完成，用时 {done.Time}");
                break;
            case RenderCompletedAll:
                EnqueueLog("全部帧完成");
                OverallProgress01 = 1;

                // 先设置状态，确保状态变化事件被触发
                SetStatus(RenderTaskStatus.Completed, "已完成");
                EndTime = DateTime.Now;
                if (StartTime.HasValue)
                {
                    Duration = EndTime.Value - StartTime.Value;
                }

                // 渲染完成后，停止 Blender 进程
                try
                {
                    // 先取消事件订阅，避免在停止过程中触发事件
                    if (_exe is not null)
                    {
                        _exe.OnOutputReceived -= HandleRawOutput;
                        _exe.OnErrorReceived -= HandleRawError;
                    }

                    _session?.Dispose();
                    _session = null;
                }
                catch (Exception ex)
                {
                    EnqueueLog($"停止渲染进程时出错: {ex.Message}");
                }

                break;
            case RenderError err:
                EnqueueLog($"错误: {err.Message}");
                SetStatus(RenderTaskStatus.Failed, "渲染失败");
                EndTime = DateTime.Now;
                if (StartTime.HasValue)
                {
                    Duration = EndTime.Value - StartTime.Value;
                }

                break;
        }
    }

    private void SetStatus(RenderTaskStatus status, string statusText)
    {
        Status = status;
        StatusText = statusText;

        // 当任务开始运行时，初始化CurrentFrame为StartFrame
        if (status == RenderTaskStatus.Running && CurrentFrame == 0)
        {
            CurrentFrame = StartFrame;
        }

        StatusChanged?.Invoke(this, new RenderTaskStatusChangedEventArgs(status, statusText));
    }

    private void EnqueueLog(string line)
    {
        if (IsLogPaused) return;

        // 简单的重复日志过滤
        if (!string.IsNullOrWhiteSpace(line) && _logQueue.Count < 500) // 防止队列过大
        {
            _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
        }
    }

    private void FlushLogQueue()
    {
        if (_logQueue.IsEmpty || IsLogPaused || _isFlushing) return;

        // 防止频繁刷新
        var now = DateTime.Now;
        if ((now - _lastFlushTime).TotalMilliseconds < MinFlushIntervalMs) return;

        lock (_logLock)
        {
            if (_isFlushing) return;
            _isFlushing = true;
        }

        try
        {
            var sb = new StringBuilder();
            int dequeued = 0;

            // 限制单次处理的日志数量，避免UI阻塞
            while (_logQueue.TryDequeue(out var line) && dequeued < MaxBatchSize)
            {
                if (dequeued++ > 0) sb.AppendLine();
                sb.Append(line);
            }

            var text = sb.ToString();
            if (string.IsNullOrEmpty(text)) return;

            _lastFlushTime = now;

            // 使用低优先级调度，减少对UI的影响
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { UpdateOutputLog(text); },
                Avalonia.Threading.DispatcherPriority.Background);
        }
        finally
        {
            _isFlushing = false;
        }
    }

    private void UpdateOutputLog(string newText)
    {
        // 将新文本追加到现有日志，并按行数限制截断最旧部分
        if (string.IsNullOrEmpty(OutputLog))
        {
            OutputLog = newText;
            _logLineCount = CountLines(OutputLog);
        }
        else
        {
            OutputLog += Environment.NewLine + newText;
            _logLineCount += CountLines(newText);
        }

        if (_logLineCount > MaxLogLines)
        {
            // 只保留最后 MaxLogLines 行，使用更高效的字符串操作
            var lines = OutputLog.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var start = Math.Max(0, lines.Length - MaxLogLines);
            OutputLog = string.Join(Environment.NewLine, lines, start, lines.Length - start);
            _logLineCount = MaxLogLines;
        }
    }

    private static int CountLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int count = 1;
        for (int i = 0; i < s.Length; i++)
            if (s[i] == '\n')
                count++;
        return count;
    }

    private void DisposeSession()
    {
        try
        {
            _session?.Dispose();
        }
        catch
        { }

        if (_exe is not null)
        {
            _exe.OnOutputReceived -= HandleRawOutput;
            _exe.OnErrorReceived -= HandleRawError;
            // 注意：不释放_exe，因为它可能被其他任务使用
        }

        _session = null;
        _exe = null;
    }

    public void ResetProgress()
    {
        OverallProgress01 = 0;
        Progress01 = 0;
        CurrentFrame = 0;
        CompletedFrames = 0;
    }

    public void Dispose()
    {
        _logTimer?.Stop();
        _logTimer?.Dispose();
        DisposeSession();
        FileInfo?.Dispose();
        RenderedImage?.Dispose();
    }

    // 转换器
    public static readonly IValueConverter StatusToColorConverter = new StatusToColorConverter();
}

// 状态到颜色的转换器
public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RenderTaskStatus status)
        {
            return status switch
            {
                RenderTaskStatus.Pending => "#FFA500", // 橙色
                RenderTaskStatus.Running => "#00FF00", // 绿色
                RenderTaskStatus.Completed => "#008000", // 深绿色
                RenderTaskStatus.Failed => "#FF0000", // 红色
                RenderTaskStatus.Cancelled => "#808080", // 灰色
                _ => "#CCCCCC" // 默认灰色
            };
        }

        return "#CCCCCC";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


// 渲染任务状态枚举
public enum RenderTaskStatus
{
    Pending, // 等待中
    Running, // 运行中
    Completed, // 已完成
    Failed, // 失败
    Cancelled // 已取消
}

// 状态变化事件参数
public class RenderTaskStatusChangedEventArgs : EventArgs
{
    public RenderTaskStatus Status { get; }
    public string StatusText { get; }

    public RenderTaskStatusChangedEventArgs(RenderTaskStatus status, string statusText)
    {
        Status = status;
        StatusText = statusText;
    }
}

// 进度变化事件参数
public class RenderTaskProgressEventArgs : EventArgs
{
    public double OverallProgress { get; }
    public double CurrentFrameProgress { get; }
    public int CurrentFrame { get; }

    public RenderTaskProgressEventArgs(double overallProgress, double currentFrameProgress, int currentFrame)
    {
        OverallProgress = overallProgress;
        CurrentFrameProgress = currentFrameProgress;
        CurrentFrame = currentFrame;
    }
}