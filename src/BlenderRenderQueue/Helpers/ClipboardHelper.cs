using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using BlenderRenderQueue.Services.UI;

namespace BlenderRenderQueue.Helpers;

public static class PowerShellClipBoard
{
    public static async Task SetImage(string imagePath)
    {
        string script = $@"
            Add-Type -AssemblyName System.Windows.Forms;
            Add-Type -AssemblyName System.Drawing;
            $image = [System.Drawing.Image]::FromFile('{imagePath}');
            $imageStream = New-Object System.IO.MemoryStream;
            $image.Save($imageStream, [System.Drawing.Imaging.ImageFormat]::Png);
            $dataObj = New-Object System.Windows.Forms.DataObject('Bitmap', $image);
            $dataObj.SetData('PNG', $imageStream);
            [System.Windows.Forms.Clipboard]::SetDataObject($dataObj, $true);
        ";

        await ExecutePowerShell(script);
    }
    
    public static async Task SetText(string text)
    {
        string script = $@"
            Add-Type -AssemblyName System.Windows.Forms;
            [System.Windows.Forms.Clipboard]::SetText('{text}');
        ";

        await ExecutePowerShell(script);
    }

    private static async Task ExecutePowerShell(string script)
    {
        string psFile = Path.GetTempFileName() + ".ps1";
        await File.WriteAllTextAsync(psFile, script);

        ProcessStartInfo startInfo = new ProcessStartInfo()
        {
            FileName = "powershell.exe",
            Arguments =
                $"-NoProfile -NoLogo -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{psFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();
        await process.WaitForExitAsync();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        if (!string.IsNullOrEmpty(error))
        {
            throw new Exception($"PowerShell Error: {error}");
        }

        File.Delete(psFile);
    }
}
// TODO use this code to replace the PowerShellClipBoard class (Faster and more reliable)
// However, some software can not paste the image from the clipboard(for example, Wechat).
public static class ClipboardHelper
{
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_DIB = 8;
    private const uint CF_DIBV5 = 17;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static bool SetPng(string srcPng)
    {
        var data = File.ReadAllBytes(srcPng);

        int size = data.Length;
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)size);
        if (hMem == IntPtr.Zero)
        {
            Console.WriteLine("Failed to allocate global memory.");
            return false;
        }

        IntPtr lpMem = GlobalLock(hMem);
        if (lpMem == IntPtr.Zero)
        {
            Console.WriteLine("Failed to lock global memory.");
            return false;
        }

        try
        {
            Marshal.Copy(data, 0, lpMem, size);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            Console.WriteLine("Failed to open clipboard.");
            return false;
        }

        try
        {
            EmptyClipboard();
            uint pngFormat = RegisterClipboardFormat("PNG");
            SetClipboardData(pngFormat, hMem);
        }
        finally
        {
            CloseClipboard();
        }

        return true;
    }

    public static bool SetDib(string srcBmp)
    {
        byte[] data = File.ReadAllBytes(srcBmp);
        byte[] output = new byte[data.Length - 14];
        Array.Copy(data, 14, output, 0, output.Length);
        int size = output.Length;

        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)size);
        if (hMem == IntPtr.Zero)
        {
            Console.WriteLine("Failed to allocate global memory.");
            return false;
        }

        IntPtr lpMem = GlobalLock(hMem);
        if (lpMem == IntPtr.Zero)
        {
            Console.WriteLine("Failed to lock global memory.");
            return false;
        }

        try
        {
            Marshal.Copy(output, 0, lpMem, size);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        uint format = output[0] switch
        {
            56 or 108 or 124 => CF_DIBV5,
            _ => CF_DIB
        };

        if (!OpenClipboard(IntPtr.Zero))
        {
            Console.WriteLine("Failed to open clipboard.");
            return false;
        }

        try
        {
            EmptyClipboard();
            SetClipboardData(format, hMem);
        }
        finally
        {
            CloseClipboard();
        }

        return true;
    }

    public static bool SetImage(string srcImg)
    {
        string extension = Path.GetExtension(srcImg).ToLower();
        return extension switch
        {
            ".bmp" => SetDib(srcImg),
            ".png" => SetPng(srcImg),
            _ => throw new NotSupportedException("Unsupported image format")
        };
    }

    public static async Task<bool> SetText(string text)
    {
        try
        {
            var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 使用Avalonia剪切板服务设置文本，通过context获取TopLevel
    /// </summary>
    /// <param name="text">要复制的文本</param>
    /// <param name="context">上下文对象，通常传入ViewModel实例</param>
    /// <returns>是否成功设置剪切板</returns>
    /// <example>
    /// // 在ViewModel中使用：
    /// var success = await ClipboardHelper.SetText("Hello World", this);
    /// 
    /// // 在View中使用：
    /// var success = await ClipboardHelper.SetText("Hello World", this);
    /// </example>
    public static async Task<bool> SetText(string text, object context)
    {
        try
        {
            var topLevel = ToplevelService.GetTopLevelForContext(context);
            
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string?> GetText()
    {
        try
        {
            var topLevel = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            
            if (topLevel?.Clipboard != null)
            {
                using var data = await topLevel.Clipboard.TryGetDataAsync();
                return data == null ? null : await data.TryGetTextAsync();
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 使用Avalonia剪切板服务获取文本，通过context获取TopLevel
    /// </summary>
    /// <param name="context">上下文对象，通常传入ViewModel实例</param>
    /// <returns>剪切板中的文本，如果获取失败则返回null</returns>
    /// <example>
    /// // 在ViewModel中使用：
    /// var text = await ClipboardHelper.GetText(this);
    /// 
    /// // 在View中使用：
    /// var text = await ClipboardHelper.GetText(this);
    /// </example>
    public static async Task<string?> GetText(object context)
    {
        try
        {
            var topLevel = ToplevelService.GetTopLevelForContext(context);
            
            if (topLevel?.Clipboard != null)
            {
                using var data = await topLevel.Clipboard.TryGetDataAsync();
                return data == null ? null : await data.TryGetTextAsync();
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }
}
