using System;
using System.Threading.Tasks;
using BlenderRenderQueue.ViewModels;

namespace BlenderRenderQueue.Services.Business.Api;

/// <summary>
/// 渲染队列API管理器
/// 负责管理API服务的生命周期和配置
/// </summary>
public class RenderQueueApiManager : IDisposable
{
    private readonly RenderQueueViewModel _renderQueue;
    private IRenderQueueApiService? _apiService;
    private bool _disposed;

    /// <summary>
    /// API是否启用
    /// </summary>
    public bool IsApiEnabled { get; set; } = true;

    /// <summary>
    /// API端口号
    /// </summary>
    public int ApiPort { get; set; } = 8325;

    /// <summary>
    /// API是否正在运行
    /// </summary>
    public bool IsApiRunning => _apiService?.IsRunning ?? false;

    /// <summary>
    /// API服务状态变化事件
    /// </summary>
    public event EventHandler<ApiServiceStatusChangedEventArgs>? ApiStatusChanged;

    public RenderQueueApiManager(RenderQueueViewModel renderQueue)
    {
        _renderQueue = renderQueue;
        
        // 如果API已启用，自动启动服务
        if (IsApiEnabled)
        {
            Console.WriteLine($"[RenderQueueApiManager] 🔧 API is enabled, preparing to auto-start service...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await StartApiAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RenderQueueApiManager] ⚠️ Failed to auto-start API service: {ex.Message}");
                }
            });
        }
        else
        {
            Console.WriteLine($"[RenderQueueApiManager] 🔧 API is disabled, skipping auto-start");
        }
    }

    /// <summary>
    /// 启动API服务
    /// </summary>
    public async Task StartApiAsync()
    {
        Console.WriteLine($"[RenderQueueApiManager] 🚀 Starting API service on port {ApiPort}...");
        
        if (!IsApiEnabled)
        {
            Console.WriteLine($"[RenderQueueApiManager] ⚠️ API is disabled, cannot start service");
            return;
        }

        if (_apiService != null && _apiService.IsRunning)
        {
            Console.WriteLine($"[RenderQueueApiManager] ⚠️ API service is already running");
            return;
        }

        try
        {
            Console.WriteLine($"[RenderQueueApiManager] 🔧 Creating API service instance...");
            _apiService = new RenderQueueApiService(_renderQueue);
            _apiService.StatusChanged += OnApiServiceStatusChanged;
            
            Console.WriteLine($"[RenderQueueApiManager] 🔧 Starting API service...");
            await _apiService.StartAsync(ApiPort);
            Console.WriteLine($"[RenderQueueApiManager] ✅ API service started successfully on port {ApiPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueApiManager] ❌ Failed to start API service: {ex.Message}");
            Console.WriteLine($"[RenderQueueApiManager] ❌ Exception details: {ex}");
            throw;
        }
    }

    /// <summary>
    /// 停止API服务
    /// </summary>
    public async Task StopApiAsync()
    {
        if (_apiService == null || !_apiService.IsRunning)
        {
            Console.WriteLine($"[RenderQueueApiManager] ℹ️ API服务未运行，无需停止");
            return;
        }

        try
        {
            Console.WriteLine($"[RenderQueueApiManager] 🛑 正在停止API服务...");
            await _apiService.StopAsync();
            Console.WriteLine($"[RenderQueueApiManager] ✅ API服务已停止");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueApiManager] ❌ 停止API服务失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 切换API服务状态
    /// </summary>
    public async Task ToggleApiAsync()
    {
        if (IsApiRunning)
        {
            await StopApiAsync();
        }
        else
        {
            await StartApiAsync();
        }
    }

    /// <summary>
    /// 设置API配置
    /// </summary>
    /// <param name="enabled">是否启用</param>
    /// <param name="port">端口号</param>
    public async Task SetApiConfigAsync(bool enabled, int port)
    {
        var wasRunning = IsApiRunning;
        
        // 如果正在运行，先停止
        if (wasRunning)
        {
            await StopApiAsync();
        }

        IsApiEnabled = enabled;
        ApiPort = port;

        // 如果启用API，则启动服务
        if (enabled)
        {
            await StartApiAsync();
        }
    }

    private void OnApiServiceStatusChanged(object? sender, ApiServiceStatusChangedEventArgs e)
    {
        ApiStatusChanged?.Invoke(this, e);
        Console.WriteLine($"[RenderQueueApiManager] API服务状态变化: {e.Message}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Console.WriteLine($"[RenderQueueApiManager] 🗑️ 正在释放API管理器资源...");
            StopApiAsync().Wait(10000); // 等待最多10秒
            Console.WriteLine($"[RenderQueueApiManager] ✅ API管理器资源已释放");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueApiManager] ⚠️ 释放API管理器资源时出现异常: {ex.Message}");
        }

        if (_apiService is IDisposable disposableApiService)
        {
            disposableApiService.Dispose();
        }

        _disposed = true;
    }
}
