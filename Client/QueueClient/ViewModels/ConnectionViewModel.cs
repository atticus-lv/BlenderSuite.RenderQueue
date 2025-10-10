using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

    [ObservableProperty]
    private string _serverUrl = "http://192.168.101.174:8325";

    [ObservableProperty]
    private List<string> _serverUrls = new();

    [ObservableProperty]
    private int _refreshInterval = 1;

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private string _serverVersion = string.Empty;

    [ObservableProperty]
    private DateTime _lastConnected = DateTime.MinValue;

    [ObservableProperty]
    private bool _autoRefreshEnabled = true;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ConnectionViewModel()
    {
        _apiService = new ApiService();
        _persistenceService = new QueueClientPersistenceService();
        _clientData = new QueueClientData();
        
        // 异步加载数据
        _ = LoadDataAsync();
    }

    /// <summary>
    /// 加载客户端数据
    /// </summary>
    private async Task LoadDataAsync()
    {
        try
        {
            _clientData = await _persistenceService.LoadDataAsync();
            
            // 更新UI属性
            ServerUrls = _clientData.ServerUrls.ToList();
            RefreshInterval = _clientData.RefreshInterval;
            AutoRefreshEnabled = _clientData.AutoRefresh;
            
            // 优先使用保存的选中URL，否则使用第一个URL
            if (!string.IsNullOrWhiteSpace(_clientData.SelectedServerUrl))
            {
                ServerUrl = _clientData.SelectedServerUrl;
            }
            else if (Enumerable.Any<string>(ServerUrls))
            {
                ServerUrl = Enumerable.First<string>(ServerUrls);
            }
            
            Console.WriteLine($"[ConnectionViewModel] 数据加载完成 - 选中URL: {ServerUrl}, 服务器列表数量: {ServerUrls.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConnectionViewModel] 加载数据失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 保存客户端数据
    /// </summary>
    private async Task SaveDataAsync()
    {
        try
        {
            // 更新数据模型
            _clientData.ServerUrls = Enumerable.ToList<string>(ServerUrls);
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
    /// 添加服务器URL并保存当前选中URL
    /// </summary>
    [RelayCommand]
    private async Task AddServerUrlAsync()
    {
        if (!string.IsNullOrWhiteSpace(ServerUrl))
        {
            // 如果URL不在列表中，添加到列表
            if (!ServerUrls.Contains(ServerUrl))
            {
                ServerUrls.Add(ServerUrl);
            }
            
            // 保存当前选中的URL和整个数据
            await SaveDataAsync();
        }
    }

    /// <summary>
    /// 移除服务器URL
    /// </summary>
    [RelayCommand]
    private async Task RemoveServerUrlAsync(string url)
    {
        if (ServerUrls.Contains(url))
        {
            ServerUrls.Remove(url);
            await SaveDataAsync();
            
            // 如果移除的是当前URL，切换到第一个可用的URL
            if (ServerUrl == url && Enumerable.Any<string>(ServerUrls))
            {
                ServerUrl = Enumerable.First<string>(ServerUrls);
            }
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
            return;

        IsConnecting = true;
        ConnectionStatus = "Connecting...";
        ErrorMessage = string.Empty; // 清除之前的错误信息

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
                    ErrorMessage = string.Empty; // 连接成功时清除错误信息
                    
                    // 连接成功后，将URL添加到服务器列表并保存
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
                ErrorMessage = "Unable to establish connection to the server. Please check the URL and ensure the server is running.";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = "Connection failed";
            
            // 提供更详细的错误信息
            var errorDetails = ex switch
            {
                HttpRequestException httpEx => $"Network error: {httpEx.Message}. Please check if the server is running and accessible.",
                TaskCanceledException => "Connection timeout. The server may be unreachable or slow to respond.",
                System.Net.Sockets.SocketException socketEx => $"Socket error: {socketEx.Message}. Check network connectivity.",
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

    /// <summary>
    /// 当刷新间隔变化时自动保存
    /// </summary>
    partial void OnRefreshIntervalChanged(int value)
    {
        _ = SaveDataAsync();
    }

    /// <summary>
    /// 当自动刷新设置变化时自动保存
    /// </summary>
    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        _ = SaveDataAsync();
    }

    /// <summary>
    /// 当服务器URL变化时自动保存
    /// </summary>
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
        if (disposing)
        {
            _apiService?.Dispose();
        }
        base.Dispose(disposing);
    }
}
