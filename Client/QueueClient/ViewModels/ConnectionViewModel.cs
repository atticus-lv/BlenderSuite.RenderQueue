using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using BlenderSuite.RenderQueue.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlenderSuite.RenderQueue.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly ApiService _apiService;

    [ObservableProperty]
    private string _serverUrl = "http://192.168.101.174:8080";

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

    public ConnectionViewModel()
    {
        _apiService = new ApiService();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
            return;

        IsConnecting = true;
        ConnectionStatus = "Connecting...";

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
                }
                else
                {
                    IsConnected = false;
                    ConnectionStatus = "Connected but no health info";
                }
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "Connection failed";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = $"Error: {ex.Message}";
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
