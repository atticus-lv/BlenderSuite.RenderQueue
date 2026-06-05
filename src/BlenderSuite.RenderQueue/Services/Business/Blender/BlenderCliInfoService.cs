using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderSuite.RenderQueue.Services.Business.Blender;

public sealed class BlenderCliInfoService : IBlenderCliInfoService
{
	public async Task<BlenderVersionInfo> GetVersionInfoAsync(string blenderExePath, CancellationToken cancellationToken = default)
	{
		var psi = new ProcessStartInfo
		{
			FileName = blenderExePath,
			Arguments = "-v",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};

		var stdout = new StringBuilder();
		var stderr = new StringBuilder();

		using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
		var tcs = new TaskCompletionSource<int>();
		p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
		p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
		p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);

		if (!p.Start()) throw new InvalidOperationException("Failed to start blender process");
		p.BeginOutputReadLine();
		p.BeginErrorReadLine();

		using var reg = cancellationToken.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
		await tcs.Task.ConfigureAwait(false);

		var text = stdout.ToString();
		if (string.IsNullOrWhiteSpace(text)) text = stderr.ToString();

		return ParseVersion(text);
	}

	private static BlenderVersionInfo ParseVersion(string output)
	{
		// 预期格式：
		// Blender 5.0.0 Alpha
		//         build date: 2025-09-18
		//         build time: 02:24:00
		//         build commit date: 2025-09-17
		//         build commit time: 23:20
		//         build hash: fb352dcd5363
		//         build branch: main
		//         build platform: Windows
		//         build type: Release
		var info = new BlenderVersionInfo();

		var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var raw in lines)
		{
			var line = raw.Trim();
			if (line.StartsWith("Blender ", StringComparison.OrdinalIgnoreCase))
			{
				// Blender 5.0.0 Alpha
				info.Product = "Blender";
				info.Version = line.Substring("Blender ".Length).Trim();
				continue;
			}

			ParseKeyValue(line, "build date:", v => info.BuildDate = DateTime.TryParse(v, out var d) ? d : null);
			ParseKeyValue(line, "build time:", v => info.BuildTime = v);
			ParseKeyValue(line, "build commit date:", v => info.CommitDate = DateTime.TryParse(v, out var d) ? d : null);
			ParseKeyValue(line, "build commit time:", v => info.CommitTime = v);
			ParseKeyValue(line, "build hash:", v => info.Hash = v);
			ParseKeyValue(line, "build branch:", v => info.Branch = v);
			ParseKeyValue(line, "build platform:", v => info.Platform = v);
			ParseKeyValue(line, "build type:", v => info.Type = v);
		}

		return info;
	}

	private static void ParseKeyValue(string line, string key, Action<string> set)
	{
		if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
		{
			var v = line.Substring(key.Length).Trim();
			set(v);
		}
	}
} 