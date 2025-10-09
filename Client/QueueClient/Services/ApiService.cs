using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
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
        if (!OperatingSystem.IsAndroid()) return new HttpClient(handler);
        Console.WriteLine("[ApiService] Configuring HttpClient for Android platform");
            
        // Allow insecure HTTP connections
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            
        // Disable proxies
        handler.UseProxy = false;

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
            Console.WriteLine($"[ApiService] Platform: Android={OperatingSystem.IsAndroid()}, Windows={OperatingSystem.IsWindows()}");
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

    public async Task<HealthResponse?> GetHealthAsync()
    {
        try
        {
            if (OperatingSystem.IsAndroid())
            {
                using var androidClient = CreateAndroidHttpClient();
                return await androidClient.GetFromJsonAsync<HealthResponse>($"{_baseUrl}/api/health");
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

    public async Task<QueueStatusResponse?> GetQueueStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/queue/status");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<QueueStatusResponse>();
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

    public async Task<List<TaskInfoResponse>?> GetTasksAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/queue/tasks");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<TaskInfoResponse>>();
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

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
