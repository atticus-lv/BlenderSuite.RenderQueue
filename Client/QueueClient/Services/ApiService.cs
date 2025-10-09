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
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<HealthResponse?> GetHealthAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<HealthResponse>($"{_baseUrl}/api/health");
        }
        catch
        {
            return null;
        }
    }

    public async Task<QueueStatusResponse?> GetQueueStatusAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<QueueStatusResponse>($"{_baseUrl}/api/queue/status");
        }
        catch
        {
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
            return null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
