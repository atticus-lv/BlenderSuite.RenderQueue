using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using BlenderSuite.RenderQueue;

namespace QueueClient.Browser;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
        .WithInterFont()
        .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
    {
        // 初始化瀏覽器特定配置
        InitializeBrowserConfig();
        
        return AppBuilder.Configure<App>();
    }
    
    private static void InitializeBrowserConfig()
    {
        // 設置瀏覽器環境變量
        Environment.SetEnvironmentVariable("BROWSER_PLATFORM", "true");
        
        // 可以在此處添加其他瀏覽器特定的初始化邏輯
        Console.WriteLine("[BrowserConfig] 瀏覽器端配置已初始化");
    }
}