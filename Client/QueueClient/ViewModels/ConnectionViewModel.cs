using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QueueClient.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly ApiService _apiService;
    private readonly QueueClientPersistenceService _persistenceService;
    private QueueClientData _clientData;

    [ObservableProperty] private string _serverUrl = "http://192.168.101.174:8325";

    [ObservableProperty] private List<string> _serverUrls = [];

    [ObservableProperty] private int _refreshInterval = 1;

    [ObservableProperty] private bool _isConnected;

    [ObservableProperty] private bool _isConnecting;

    [ObservableProperty] private string _connectionStatus = "Disconnected";

    [ObservableProperty] private string _serverVersion = string.Empty;

    [ObservableProperty] private DateTime _lastConnected = DateTime.MinValue;

    [ObservableProperty] private bool _autoRefreshEnabled = true;

    [ObservableProperty] private string _errorMessage = string.Empty;

    public ConnectionViewModel()
    {
        _apiService = new ApiService();
        _persistenceService = new QueueClientPersistenceService();
        _clientData = new QueueClientData();

        _ = LoadDataAsync();
    }


    private async Task LoadDataAsync()
    {
        try
        {
            _clientData = await _persistenceService.LoadDataAsync();

            ServerUrls = _clientData.ServerUrls.ToList();
            RefreshInterval = _clientData.RefreshInterval;
            AutoRefreshEnabled = _clientData.AutoRefresh;

            if (!string.IsNullOrWhiteSpace(_clientData.SelectedServerUrl))
                ServerUrl = _clientData.SelectedServerUrl;
            else if (ServerUrls.Any()) ServerUrl = ServerUrls.First();

            Console.WriteLine($"[ConnectionViewModel] 数据加载完成 - 选中URL: {ServerUrl}, 服务器列表数量: {ServerUrls.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConnectionViewModel] 加载数据失败: {ex.Message}");
        }
    }


    private async Task SaveDataAsync()
    {
        try
        {
            // 更新数据模型
            _clientData.ServerUrls = ServerUrls.ToList();
            _clientData.SelectedServerUrl = ServerUrl;
            _clientData.RefreshInterval = RefreshInterval;
            _clientData.AutoRefresh = AutoRefreshEnabled;

            await _persistenceService.SaveDataAsync(_clientData);
            Console.WriteLine($"[ConnectionViewModel] 数据保存完成 - 选中URL: {ServerUrl}, 服务器列表数量: {ServerUrls.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConnectionViewModel] 保存数据失败: {ex.Message}");
        }
    }

    /// <summary>
    ///     添加服务器URL并保存当前选中URL
    /// </summary>
    [RelayCommand]
    private async Task AddServerUrlAsync()
    {
        if (!string.IsNullOrWhiteSpace(ServerUrl))
        {
            // 如果URL不在列表中，添加到列表
            if (!ServerUrls.Contains(ServerUrl)) ServerUrls.Add(ServerUrl);

            // 保存当前选中的URL和整个数据
            await SaveDataAsync();
        }
    }

    /// <summary>
    ///     移除服务器URL
    /// </summary>
    [RelayCommand]
    private async Task RemoveServerUrlAsync(string url)
    {
        if (ServerUrls.Contains(url))
        {
            ServerUrls.Remove(url);
            await SaveDataAsync();

            if (ServerUrl == url && ServerUrls.Count != 0) ServerUrl = ServerUrls.First();
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
            return;

        IsConnecting = true;
        ConnectionStatus = "Connecting...";
        ErrorMessage = string.Empty;

        try
        {
            _apiService.SetBaseUrl(ServerUrl);

            var isConnected = await _apiService.CheckConnectionAsync();
            if (isConnected)
            {
                var health = await _apiService.GetHealthAsync();
                if (health != null)
                {
                    IsConnected = true;
                    ConnectionStatus = "Connected";
                    ServerVersion = health.Version;
                    LastConnected = DateTime.Now;
                    ErrorMessage = string.Empty;

                    await AddServerUrlAsync();
                }
                else
                {
                    IsConnected = false;
                    ConnectionStatus = "Connected but no health info";
                    ErrorMessage = "Server responded but health information is unavailable";
                }
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "Connection failed";
                ErrorMessage =
                    "Unable to establish connection to the server. Please check the URL and ensure the server is running.";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = "Connection failed";

            var errorDetails = ex switch
            {
                HttpRequestException httpEx =>
                    $"Network error: {httpEx.Message}. Please check if the server is running and accessible.",
                TaskCanceledException => "Connection timeout. The server may be unreachable or slow to respond.",
                SocketException socketEx =>
                    $"Socket error: {socketEx.Message}. Check network connectivity.",
                _ => $"Connection error: {ex.GetType().Name}: {ex.Message}"
            };

            ErrorMessage = errorDetails;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        IsConnected = false;
        ConnectionStatus = "Disconnected";
        ServerVersion = string.Empty;
        ErrorMessage = string.Empty; // 断开连接时清除错误信息
    }


    partial void OnRefreshIntervalChanged(int value)
    {
        _ = SaveDataAsync();
    }


    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        _ = SaveDataAsync();
    }


    partial void OnServerUrlChanged(string value)
    {
        _ = SaveDataAsync();
    }

    public ApiService GetApiService()
    {
        return _apiService;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _apiService?.Dispose();

        base.Dispose(disposing);
    }
}