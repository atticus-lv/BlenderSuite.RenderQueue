using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;

namespace BlenderSuite.RenderQueue.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private string _baseUrl = string.Empty;

    public ApiService()
    {
        _httpClient = CreateHttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler();
        
        // Android
        if (OperatingSystem.IsAndroid())
        {
            Console.WriteLine("[ApiService] Configuring HttpClient for Android platform");
            
            // Allow insecure HTTP connections
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                
            // Disable proxies
            handler.UseProxy = false;

            return new HttpClient(handler);
        }
        
        // Browser (WASM)
        if (OperatingSystem.IsBrowser())
        {
            Console.WriteLine("[ApiService] Configuring HttpClient for Browser platform");
            
            // 浏览器环境使用默认配置，但设置适当的超时
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15);
            
            // 设置User-Agent
            client.DefaultRequestHeaders.Add("User-Agent", "QueueClient-Browser/1.0");
            
            return client;
        }
        
        // Desktop/其他平台
        return new HttpClient(handler);
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            Console.WriteLine($"[ApiService] Attempting to connect to: {_baseUrl}/api/health");
            Console.WriteLine($"[ApiService] Platform: Android={OperatingSystem.IsAndroid()}, Browser={OperatingSystem.IsBrowser()}, Windows={OperatingSystem.IsWindows()}");
            Console.WriteLine($"[ApiService] HttpClient timeout: {_httpClient.Timeout}");
            
            // 对于Android，尝试使用更简单的请求方式
            if (OperatingSystem.IsAndroid())
            {
                Console.WriteLine("[ApiService] Using Android-specific connection method");
                
                // 创建一个新的HttpClient实例，避免可能的缓存问题
                using var androidClient = CreateAndroidHttpClient();
                var response = await androidClient.GetAsync($"{_baseUrl}/api/health");
                Console.WriteLine($"[ApiService] Android response status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            // 对于浏览器环境，使用特殊的连接方法
            else if (OperatingSystem.IsBrowser())
            {
                Console.WriteLine("[ApiService] Using Browser-specific connection method");
                
                // 创建一个新的HttpClient实例，避免可能的缓存问题
                using var browserClient = CreateBrowserHttpClient();
                var response = await browserClient.GetAsync($"{_baseUrl}/api/health");
                Console.WriteLine($"[ApiService] Browser response status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            else
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/health");
                Console.WriteLine($"[ApiService] Response status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService] CheckConnectionAsync exception: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[ApiService] Stack trace: {ex.StackTrace}");
            return false;
        }
    }

    private HttpClient CreateAndroidHttpClient()
    {
        var handler = new HttpClientHandler();
        
        // Android特定的HTTP配置
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        handler.UseProxy = false;
        
        var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(15);
        
        // 设置User-Agent
        client.DefaultRequestHeaders.Add("User-Agent", "QueueClient-Android/1.0");
        
        return client;
    }

    private HttpClient CreateBrowserHttpClient()
    {
        var handler = new HttpClientHandler();
        
        // 浏览器特定的HTTP配置
        var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(15);
        
        // 设置User-Agent
        client.DefaultRequestHeaders.Add("User-Agent", "QueueClient-Browser/1.0");
        
        // 设置Accept头
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        
        // 设置CORS相关头部
        client.DefaultRequestHeaders.Add("Access-Control-Request-Method", "GET");
        client.DefaultRequestHeaders.Add("Access-Control-Request-Headers", "Content-Type");
        
        return client;
    }

    public async Task<HealthResponse?> GetHealthAsync()
    {
        try
        {
            if (OperatingSystem.IsAndroid())
            {
                using var androidClient = CreateAndroidHttpClient();
                return await androidClient.GetFromJsonAsync<HealthResponse>($"{_baseUrl}/api/health");
            }
            else if (OperatingSystem.IsBrowser())
            {
                using var browserClient = CreateBrowserHttpClient();
                return await browserClient.GetFromJsonAsync<HealthResponse>($"{_baseUrl}/api/health");
            }
            else
            {
                return await _httpClient.GetFromJsonAsync<HealthResponse>($"{_baseUrl}/api/health");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService] GetHealthAsync exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<OptimizedQueueStatusResponse?> GetQueueStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/queue/status");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<OptimizedQueueStatusResponse>();
            }
            else
            {
                Console.WriteLine($"[ApiService] GetQueueStatusAsync failed: {response.StatusCode} - {response.ReasonPhrase}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService] GetQueueStatusAsync exception: {ex.Message}");
            return null;
        }
    }

    public async Task<List<OptimizedTaskInfo>?> GetTasksAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/queue/tasks");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<OptimizedTaskInfo>>();
            }
            else
            {
                Console.WriteLine($"[ApiService] GetTasksAsync failed: {response.StatusCode} - {response.ReasonPhrase}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService] GetTasksAsync exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 开始监听进度更新流
    /// </summary>
    /// <param name="onProgressUpdate">进度更新回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartProgressStreamAsync(
        Action<OptimizedProgressUpdate> onProgressUpdate, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
            
            var response = await httpClient.GetAsync($"{_baseUrl}/api/queue/progress-stream", 
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[ApiService] Progress stream failed: {response.StatusCode}");
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var json = line.Substring(6); // 移除 "data: " 前缀
                    try
                    {
                        var updates = System.Text.Json.JsonSerializer.Deserialize<List<OptimizedProgressUpdate>>(json);
                        if (updates != null)
                        {
                            foreach (var update in updates)
                            {
                                onProgressUpdate(update);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ApiService] Failed to parse progress update: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ApiService] Progress stream exception: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
