using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Helpers;

public static class BlenderLocator
{
	public static bool TryFindBlenderExe(out string path)
	{
		path = string.Empty;
		if (!OperatingSystem.IsWindows()) return false;

		// 1) 常见注册表位置：HKLM/HKCU 下的 Blender Foundation、卸载项、文件关联
		string?[] candidates =
		[
			ReadRegString(RegistryHive.LocalMachine, @"SOFTWARE\\BlenderFoundation\\Blender", "InstallPath"),
			ReadRegString(RegistryHive.CurrentUser, @"SOFTWARE\\BlenderFoundation\\Blender", "InstallPath"),
			ReadUninstallForDisplayName("Blender"),
			ReadRegString(RegistryHive.LocalMachine, @"SOFTWARE\\Classes\\blender\\shell\\open\\command", null),
			ReadRegString(RegistryHive.CurrentUser, @"SOFTWARE\\Classes\\blender\\shell\\open\\command", null),
			ReadRegString(RegistryHive.LocalMachine, @"SOFTWARE\\WOW6432Node\\BlenderFoundation\\Blender", "InstallPath"),
		];

		foreach (var c in candidates.Where(s => !string.IsNullOrWhiteSpace(s)))
		{
			var exe = NormalizeToExe(c!);
			if (File.Exists(exe)) { path = exe; return true; }
		}

		// 2) 常见安装目录扫描（同步版本不做，以免阻塞）
		return false;
	}

	public static async Task<string?> FindBlenderExeAsync(CancellationToken cancellationToken = default)
	{
		if (!OperatingSystem.IsWindows()) return null;
		if (TryFindBlenderExe(out var path)) return path;

		return await Task.Run(() =>
		{
			try
			{
				var programFiles = new[]
				{
					Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
					Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
				}.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

				foreach (var root in programFiles)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var baseDir = Path.Combine(root!, "Blender Foundation");
					if (!Directory.Exists(baseDir)) continue;
					// 限制搜索深度与数量以降低耗时
					foreach (var d in Directory.GetDirectories(baseDir))
					{
						cancellationToken.ThrowIfCancellationRequested();
						var exe = Directory.EnumerateFiles(d, "blender.exe", SearchOption.AllDirectories).FirstOrDefault();
						if (!string.IsNullOrEmpty(exe)) return exe;
					}
				}
			}
			catch { }
			return null;
		}, cancellationToken);
	}

	private static string NormalizeToExe(string raw)
	{
		var s = raw.Trim();
		// 如果来自关联命令，可能是类似: "C:\\Program Files\\Blender Foundation\\Blender 4.0\\blender.exe" "%1"
		if (s.Contains(".exe", StringComparison.OrdinalIgnoreCase))
		{
			var idx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
			s = s.Substring(0, idx + 4);
			s = s.Trim('"');
			return s;
		}
		// 如果是安装目录，拼接 blender.exe
		if (Directory.Exists(s))
		{
			var exe = Path.Combine(s, "blender.exe");
			return exe;
		}
		return s;
	}

	private static string? ReadRegString(RegistryHive hive, string subKey, string? valueName)
	{
		try
		{
			using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(subKey);
			var v = valueName is null ? key?.GetValue(null)?.ToString() : key?.GetValue(valueName)?.ToString();
			if (!string.IsNullOrWhiteSpace(v)) return v;
			using var key32 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32).OpenSubKey(subKey);
			v = valueName is null ? key32?.GetValue(null)?.ToString() : key32?.GetValue(valueName)?.ToString();
			return string.IsNullOrWhiteSpace(v) ? null : v;
		}
		catch { return null; }
	}

	private static string? ReadUninstallForDisplayName(string namePart)
	{
		try
		{
			string[] subKeys =
			[
				@"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
				@"SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
			];
			foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
			{
				using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
				foreach (var sub in subKeys)
				{
					using var key = baseKey.OpenSubKey(sub);
					if (key is null) continue;
					foreach (var name in key.GetSubKeyNames())
					{
						using var app = key.OpenSubKey(name);
						var display = app?.GetValue("DisplayName")?.ToString() ?? string.Empty;
						if (display.Contains(namePart, StringComparison.OrdinalIgnoreCase))
						{
							var loc = app?.GetValue("InstallLocation")?.ToString();
							if (!string.IsNullOrWhiteSpace(loc)) return loc;
						}
					}
				}
			}
		}
		catch { }
		return null;
	}
} 