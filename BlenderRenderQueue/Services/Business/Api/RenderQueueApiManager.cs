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
    private bool _isStarting = false;

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
        
        // 注意：不在这里自动启动API服务，等待显式调用
        Console.WriteLine($"[RenderQueueApiManager] 🔧 API Manager initialized, API enabled: {IsApiEnabled}, Port: {ApiPort}");
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

        if (_isStarting)
        {
            Console.WriteLine($"[RenderQueueApiManager] ⚠️ API service is already starting, please wait...");
            return;
        }

        if (_apiService != null && _apiService.IsRunning)
        {
            Console.WriteLine($"[RenderQueueApiManager] ⚠️ API service is already running on port {_apiService.Port}");
            return;
        }

        _isStarting = true;

        // 如果服务存在但未运行，先清理
        if (_apiService != null && !_apiService.IsRunning)
        {
            Console.WriteLine($"[RenderQueueApiManager] 🧹 Cleaning up previous API service instance...");
            try
            {
                _apiService.StatusChanged -= OnApiServiceStatusChanged;
                if (_apiService is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RenderQueueApiManager] ⚠️ Error cleaning up previous service: {ex.Message}");
            }
            _apiService = null;
        }

        try
        {
            Console.WriteLine($"[RenderQueueApiManager] 🔧 Creating new API service instance...");
            _apiService = new RenderQueueApiService(_renderQueue);
            _apiService.StatusChanged += OnApiServiceStatusChanged;
            
            Console.WriteLine($"[RenderQueueApiManager] 🔧 Starting API service on port {ApiPort}...");
            await _apiService.StartAsync(ApiPort);
            Console.WriteLine($"[RenderQueueApiManager] ✅ API service started successfully on port {ApiPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RenderQueueApiManager] ❌ Failed to start API service: {ex.Message}");
            Console.WriteLine($"[RenderQueueApiManager] ❌ Exception details: {ex}");
            
            // 清理失败的服务实例
            if (_apiService != null)
            {
                try
                {
                    _apiService.StatusChanged -= OnApiServiceStatusChanged;
                    if (_apiService is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch
                {
                    // 忽略清理时的异常
                }
                _apiService = null;
            }
            
            throw;
        }
        finally
        {
            _isStarting = false;
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
