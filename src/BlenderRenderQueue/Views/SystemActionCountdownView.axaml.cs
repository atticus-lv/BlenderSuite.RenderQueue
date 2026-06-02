using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BlenderRenderQueue.Extensions;
using SukiUI.Controls;

namespace BlenderRenderQueue.Views;

public partial class SystemActionCountdownView : SukiWindow
{
    private TaskCompletionSource<bool> _tcs = new();
    private CancellationTokenSource _cancellationTokenSource = new();

    public SystemActionCountdownView()
    {
        InitializeComponent();
    }

    public SystemActionCountdownView(string actionType, int countdownSeconds) : this()
    {
        TitleText.Text = string.Format(Localizer.Localizer.Instance["SystemControl_CountdownMessage"], actionType);
        CountdownText.Text = countdownSeconds.ToString();
        
        CancelButton.Click += OnCancelButtonClick;
    }
    
    public void StartCountdown(int countdownSeconds)
    {
        // 重新创建 CancellationTokenSource
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        
        // 启动倒计时
        StartCountdownAsync(countdownSeconds).FireAndForget(
            source: nameof(SystemActionCountdownView),
            message: "系统动作倒计时后台任务失败。");
    }

    private void OnCancelButtonClick(object? sender, RoutedEventArgs e)
    {
        _cancellationTokenSource.Cancel();
        Close();
        _tcs.SetResult(true); // 返回 true 表示用户取消了操作
    }

    private async Task StartCountdownAsync(int countdownSeconds)
    {
        for (int i = countdownSeconds; i > 0 && !_cancellationTokenSource.Token.IsCancellationRequested; i--)
        {
            CountdownText.Text = i.ToString();
            
            try
            {
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            // 倒计时结束，关闭对话框
            Close();
            _tcs.SetResult(false); // 返回 false 表示倒计时结束，没有取消
        }
    }

    public Task<bool> ShowDialogAsync(Window? parentWindow = null, int countdownSeconds = 60)
    {
        // 每次调用时创建新的 TaskCompletionSource
        _tcs = new TaskCompletionSource<bool>();
        
        // 启动倒计时
        StartCountdown(countdownSeconds);
        
        if (parentWindow != null)
        {
            ShowDialog(parentWindow);
        }
        else
        {
            Show();
        }

        return _tcs.Task;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellationTokenSource?.Dispose();
        base.OnClosed(e);
    }
}
