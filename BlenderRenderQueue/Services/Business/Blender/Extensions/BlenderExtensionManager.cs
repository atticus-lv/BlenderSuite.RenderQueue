using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BlenderRenderQueue.Services.Business.Blender.Extensions;

public sealed class BlenderExtensionManager : IBlenderExtensionManager
{
    private const string ExtensionId = "BlenderRenderQueue";
    private const string LegacyExtensionId = "BlenderRenderQueueSender";
    private const string UserDefaultRepoId = "user_default";

    public async Task<BlenderExtensionInstallResult> EnsureInstalledAsync(
        string blenderExecutablePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blenderExecutablePath) || !File.Exists(blenderExecutablePath))
        {
            return new BlenderExtensionInstallResult
            {
                Outcome = BlenderExtensionInstallOutcome.Skipped,
                BlenderExecutablePath = blenderExecutablePath,
                Message = "No valid Blender executable was selected."
            };
        }

        BlenderExtensionPackageInfo packageInfo;
        try
        {
            packageInfo = await ResolveBundledPackageAsync(blenderExecutablePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new BlenderExtensionInstallResult
            {
                Outcome = BlenderExtensionInstallOutcome.Failed,
                BlenderExecutablePath = blenderExecutablePath,
                Message = $"Failed to resolve bundled extension package: {ex.Message}"
            };
        }

        try
        {
            var repositories = await ReadRepositoriesAsync(blenderExecutablePath, cancellationToken);
            if (!repositories.TryGetValue(UserDefaultRepoId, out var userDefaultRepo) ||
                string.IsNullOrWhiteSpace(userDefaultRepo))
            {
                return new BlenderExtensionInstallResult
                {
                    Outcome = BlenderExtensionInstallOutcome.Failed,
                    BlenderExecutablePath = blenderExecutablePath,
                    PackageInfo = packageInfo,
                    Message = "Unable to locate Blender user_default extension repository."
                };
            }

            var installedPackages = FindInstalledPackages(repositories.Values);
            var currentPackage = installedPackages.FirstOrDefault(p => string.Equals(p.Id, packageInfo.Id, StringComparison.Ordinal));
            var legacyPackage = installedPackages.FirstOrDefault(p => string.Equals(p.Id, LegacyExtensionId, StringComparison.Ordinal));

            if (currentPackage != null && CompareVersions(currentPackage.Version, packageInfo.Version) >= 0)
            {
                if (legacyPackage != null)
                {
                    await RemovePackageAsync(blenderExecutablePath, LegacyExtensionId, cancellationToken);
                }

                return new BlenderExtensionInstallResult
                {
                    Outcome = BlenderExtensionInstallOutcome.AlreadyUpToDate,
                    BlenderExecutablePath = blenderExecutablePath,
                    PreviousVersion = currentPackage.Version,
                    InstalledVersion = currentPackage.Version,
                    PackageInfo = packageInfo,
                    Message = $"{packageInfo.Name} {currentPackage.Version} is already installed."
                };
            }

            if (currentPackage != null)
            {
                await RemovePackageAsync(blenderExecutablePath, currentPackage.Id, cancellationToken);
            }

            if (legacyPackage != null)
            {
                await RemovePackageAsync(blenderExecutablePath, legacyPackage.Id, cancellationToken);
            }

            Directory.CreateDirectory(userDefaultRepo);
            await InstallPackageAsync(blenderExecutablePath, packageInfo.PackagePath, cancellationToken);

            var installedPackagesAfterInstall = FindInstalledPackages(repositories.Values);
            var installedPackage = installedPackagesAfterInstall.FirstOrDefault(
                p => string.Equals(p.Id, packageInfo.Id, StringComparison.Ordinal));

            return new BlenderExtensionInstallResult
            {
                Outcome = currentPackage == null ? BlenderExtensionInstallOutcome.Installed : BlenderExtensionInstallOutcome.Updated,
                BlenderExecutablePath = blenderExecutablePath,
                PreviousVersion = currentPackage?.Version ?? legacyPackage?.Version,
                InstalledVersion = installedPackage?.Version ?? packageInfo.Version,
                PackageInfo = packageInfo,
                Message = currentPackage == null
                    ? $"Installed {packageInfo.Name} {packageInfo.Version}."
                    : $"Updated {packageInfo.Name} from {currentPackage.Version} to {packageInfo.Version}."
            };
        }
        catch (Exception ex)
        {
            return new BlenderExtensionInstallResult
            {
                Outcome = BlenderExtensionInstallOutcome.Failed,
                BlenderExecutablePath = blenderExecutablePath,
                PackageInfo = packageInfo,
                Message = $"Extension install failed: {ex.Message}"
            };
        }
    }

    private async Task<BlenderExtensionPackageInfo> ResolveBundledPackageAsync(
        string blenderExecutablePath,
        CancellationToken cancellationToken)
    {
        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "BlenderExtensions");
        if (Directory.Exists(bundledDirectory))
        {
            var candidate = Directory.EnumerateFiles(bundledDirectory, $"{ExtensionId}-*.zip", SearchOption.TopDirectoryOnly)
                .Select(ReadPackageInfoFromZip)
                .OrderByDescending(p => ParseVersion(p.Version))
                .FirstOrDefault();

            if (candidate != null)
            {
                return candidate;
            }
        }

        var sourceDirectory = FindExtensionSourceDirectory();
        if (sourceDirectory == null)
        {
            throw new FileNotFoundException("Could not find a bundled extension zip or extension source directory.");
        }

        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlenderRenderQueue",
            "ExtensionsCache");
        Directory.CreateDirectory(outputDirectory);

        await RunBlenderCommandAsync(
            blenderExecutablePath,
            $"--background --command extension build --source-dir {Quote(sourceDirectory)} --output-dir {Quote(outputDirectory)}",
            cancellationToken);

        var builtPackagePath = Directory.EnumerateFiles(outputDirectory, $"{ExtensionId}-*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (builtPackagePath == null)
        {
            throw new FileNotFoundException("Blender built the extension package but no zip was produced.");
        }

        return ReadPackageInfoFromZip(builtPackagePath);
    }

    private static string? FindExtensionSourceDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", "BlenderExtensions", ExtensionId);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static BlenderExtensionPackageInfo ReadPackageInfoFromZip(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.Entries.FirstOrDefault(
            entry => string.Equals(Path.GetFileName(entry.FullName), "blender_manifest.toml", StringComparison.OrdinalIgnoreCase));

        if (manifestEntry == null)
        {
            throw new InvalidDataException($"Extension package {packagePath} does not contain blender_manifest.toml.");
        }

        using var stream = manifestEntry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var manifest = ParseTomlManifest(reader.ReadToEnd());

        return new BlenderExtensionPackageInfo
        {
            Id = manifest.TryGetValue("id", out var id) ? id : ExtensionId,
            Name = manifest.TryGetValue("name", out var name) ? name : ExtensionId,
            Version = manifest.TryGetValue("version", out var version) ? version : "0.0.0",
            PackagePath = packagePath,
            ManifestSource = packagePath
        };
    }

    private async Task<Dictionary<string, string>> ReadRepositoriesAsync(string blenderExecutablePath, CancellationToken cancellationToken)
    {
        var output = await RunBlenderCommandAsync(
            blenderExecutablePath,
            "--background --command extension repo-list",
            cancellationToken);

        var repositories = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentRepoId = null;

        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (!line.StartsWith(' ') && line.EndsWith(':'))
            {
                currentRepoId = line.TrimEnd(':').Trim();
                continue;
            }

            if (currentRepoId == null)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("directory:", StringComparison.Ordinal))
            {
                var directory = trimmed["directory:".Length..].Trim().Trim('"');
                repositories[currentRepoId] = directory;
            }
        }

        return repositories;
    }

    private static List<InstalledExtensionInfo> FindInstalledPackages(IEnumerable<string> repositoryDirectories)
    {
        var result = new List<InstalledExtensionInfo>();

        foreach (var repositoryDirectory in repositoryDirectories.Where(Directory.Exists))
        {
            foreach (var manifestPath in Directory.EnumerateFiles(repositoryDirectory, "blender_manifest.toml", SearchOption.AllDirectories))
            {
                try
                {
                    var manifest = ParseTomlManifest(File.ReadAllText(manifestPath));
                    if (!manifest.TryGetValue("id", out var id))
                    {
                        continue;
                    }

                    result.Add(new InstalledExtensionInfo
                    {
                        Id = id,
                        Version = manifest.TryGetValue("version", out var version) ? version : "0.0.0",
                        ManifestPath = manifestPath
                    });
                }
                catch
                {
                    // Ignore malformed manifests in unrelated repositories.
                }
            }
        }

        return result
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(p => ParseVersion(p.Version)).First())
            .ToList();
    }

    private async Task RemovePackageAsync(string blenderExecutablePath, string packageId, CancellationToken cancellationToken)
    {
        await RunBlenderCommandAsync(
            blenderExecutablePath,
            $"--background --command extension remove {Quote(packageId)}",
            cancellationToken);
    }

    private async Task InstallPackageAsync(string blenderExecutablePath, string packagePath, CancellationToken cancellationToken)
    {
        await RunBlenderCommandAsync(
            blenderExecutablePath,
            $"--background --command extension install-file {Quote(packagePath)} -r {UserDefaultRepoId} -e",
            cancellationToken);
    }

    private static Dictionary<string, string> ParseTomlManifest(string manifestContent)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var regex = new Regex(@"^(?<key>[A-Za-z0-9_]+)\s*=\s*""(?<value>.*)""\s*$");

        foreach (var rawLine in manifestContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
            {
                continue;
            }

            var match = regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            values[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        return values;
    }

    private static int CompareVersions(string left, string right)
    {
        return ParseVersion(left).CompareTo(ParseVersion(right));
    }

    private static Version ParseVersion(string version)
    {
        return Version.TryParse(version, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private async Task<string> RunBlenderCommandAsync(
        string blenderExecutablePath,
        string arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = blenderExecutablePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Blender process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore process termination errors during cancellation.
            }
        });

        var exitCode = await tcs.Task.ConfigureAwait(false);
        var combinedOutput = stdout.ToString().Trim();
        var combinedError = stderr.ToString().Trim();

        if (exitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(combinedError)
                ? combinedOutput
                : $"{combinedOutput}{Environment.NewLine}{combinedError}".Trim();
            throw new InvalidOperationException(message);
        }

        return string.IsNullOrWhiteSpace(combinedOutput) ? combinedError : combinedOutput;
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed class InstalledExtensionInfo
    {
        public required string Id { get; init; }
        public required string Version { get; init; }
        public required string ManifestPath { get; init; }
    }
}
