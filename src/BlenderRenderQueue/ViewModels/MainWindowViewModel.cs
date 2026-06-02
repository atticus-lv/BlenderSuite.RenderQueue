using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using BlenderRenderQueue.Services.Application.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace BlenderRenderQueue.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{	
	private readonly IRenderLogService _logService;
	
	[ObservableProperty]
	private string _appVersion = "Unknown";
	public ViewModelBase Content { get; }
	public ISukiDialogManager DialogManager { get; } = new SukiDialogManager();
	public ISukiToastManager ToastManager { get; } = new SukiToastManager();

	public MainWindowViewModel(MainRenderViewModel mainRenderViewModel, IRenderLogService logService)
	{
		_logService = logService;
		Content = mainRenderViewModel;
		GetFileVersion();
	}
	
	
	private void GetFileVersion()
	{
        if (OperatingSystem.IsWindows())
        {
            var exeDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
            var exePath = exeDir != null ? Directory.GetFiles(exeDir, "*.exe").FirstOrDefault() : null;
            _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"Executable Path: {exePath}", source: "MainWindowViewModel");
            if (exePath == null) return;
            var version = FileVersionInfo.GetVersionInfo(exePath).FileVersion;
            _logService.Write(RenderLogLevel.Info, RenderLogScope.System, $"Version: {version}", source: "MainWindowViewModel");
            if (version == null) return;
            AppVersion = version;
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
        // 尝试多个可能的路径
        var possiblePaths = new[]
        {
            // 发布环境路径
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Info.plist"),
            // 开发环境路径
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Contents", "Info.plist"),
            // 直接在当前目录查找
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Info.plist")
        };

        foreach (var bundlePath in possiblePaths)
        {
            if (!File.Exists(bundlePath)) continue;
            
            try
            {
                var plistContent = File.ReadAllText(bundlePath);
                // 简单解析plist文件获取版本号
                var versionStart = plistContent.IndexOf("<key>CFBundleShortVersionString</key>");
                if (versionStart != -1)
                {
                    var versionValueStart = plistContent.IndexOf("<string>", versionStart) + 8;
                    var versionValueEnd = plistContent.IndexOf("</string>", versionValueStart);
                    if (versionValueStart != -1 && versionValueEnd != -1)
                    {
                        AppVersion = plistContent.Substring(versionValueStart, versionValueEnd - versionValueStart);
                        return;
                    }
                }
            }
            catch
            {
                continue;
            }
        }
        
        // 如果所有路径都失败，设置为开发版本号
        AppVersion = "Dev";
        return;
        }

		AppVersion = "Unknown";
	}
	
}
