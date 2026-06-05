using Avalonia;
using System;
using System.IO;
using System.Text.Json;
using BlenderSuite.RenderQueue.Models;
using BlenderSuite.RenderQueue.Services.Application;
using BlenderSuite.RenderQueue.Services.Application.Logging;
using BlenderSuite.RenderQueue.Services.Business.Persistence;

namespace BlenderSuite.RenderQueue;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        UnhandledExceptionGuard.Register();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var appBuilder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .WithDeveloperTools()
            .LogToTrace();

        if (OperatingSystem.IsWindows())
        {
            var renderingMode = GetRenderingModeFromSettings();
            appBuilder = appBuilder.With(new Win32PlatformOptions
            {
                RenderingMode = [renderingMode]
            });
        }

        return appBuilder;
    }

    /// <summary>
    /// 从设置文件中读取硬件加速设置，决定使用哪种渲染模式
    /// </summary>
    /// <returns>渲染模式</returns>
    private static Win32RenderingMode GetRenderingModeFromSettings()
    {
        try
        {
            var settingsFilePath = Path.Combine(
                ApplicationPaths.GetAppDataDirectory(),
                "settings.json"
            );

            if (!File.Exists(settingsFilePath))
            {
                Console.WriteLine("[Program] Settings file not found, using default hardware acceleration (AngleEgl)");
                return Win32RenderingMode.AngleEgl;
            }

            var json = File.ReadAllText(settingsFilePath);
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsData);

            if (settings?.UseGpu == true)
            {
                Console.WriteLine("[Program] Hardware acceleration enabled, using WGL rendering");
                return Win32RenderingMode.AngleEgl;
            }

            Console.WriteLine("[Program] Hardware acceleration disabled, using Angle EGL rendering");
            return Win32RenderingMode.Software;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] Error reading settings, using default hardware acceleration (WGL): {ex.Message}");
            return Win32RenderingMode.Software;
        }
    }
}
