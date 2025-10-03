using System;
using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Models;

/// <summary>
/// Blender可执行文件信息模型
/// </summary>
public class BlenderExecutable
{
    [JsonPropertyName("Path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("Version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("Platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("Branch")]
    public string Branch { get; set; } = string.Empty;

    [JsonPropertyName("Hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("BuildDate")]
    public DateTime? BuildDate { get; set; }

    [JsonPropertyName("BuildTime")]
    public string BuildTime { get; set; } = string.Empty;

    [JsonPropertyName("CommitDate")]
    public DateTime? CommitDate { get; set; }

    [JsonPropertyName("CommitTime")]
    public string CommitTime { get; set; } = string.Empty;

    [JsonPropertyName("Type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("IsValid")]
    public bool IsValid { get; set; } = false;

    [JsonPropertyName("LastValidated")]
    public DateTime? LastValidated { get; set; }

    [JsonPropertyName("DisplayName")]
    public string DisplayName => GetDisplayName();

    [JsonPropertyName("FormattedPath")]
    public string FormattedPath => GetFormattedPath();

    [JsonPropertyName("VersionBranchDisplay")]
    public string VersionBranchDisplay => GetVersionBranchDisplay();

    [JsonPropertyName("BuildDateTimeDisplay")]
    public string BuildDateTimeDisplay => GetBuildDateTimeDisplay();

    /// <summary>
    /// 获取显示名称
    /// </summary>
    private string GetDisplayName()
    {
        if (string.IsNullOrEmpty(Path))
            return "未知";

        var fileName = System.IO.Path.GetFileNameWithoutExtension(Path);
        var directory = System.IO.Path.GetDirectoryName(Path);
        
        if (string.IsNullOrEmpty(directory))
            return fileName;

        // 尝试从路径中提取版本信息
        var parentDir = System.IO.Path.GetFileName(directory);
        if (!string.IsNullOrEmpty(Version))
        {
            return $"Blender {Version} ({parentDir})";
        }
        
        return $"{fileName} ({parentDir})";
    }

    /// <summary>
    /// 获取格式化的路径显示（头尾显示）
    /// </summary>
    public string GetFormattedPath(int maxLength = 50)
    {
        if (string.IsNullOrEmpty(Path))
            return "未知路径";

        if (Path.Length <= maxLength)
            return Path;

        // 使用简单的头尾显示
        var startLength = Math.Max(15, maxLength / 3); // 至少显示15个字符的开头
        var endLength = Math.Max(20, maxLength / 3);   // 至少显示20个字符的结尾
        
        // 确保不会超出路径长度
        startLength = Math.Min(startLength, Path.Length - endLength - 3);
        endLength = Math.Min(endLength, Path.Length - startLength - 3);
        
        if (startLength <= 0 || endLength <= 0)
        {
            // 如果无法合理分割，只显示文件名
            var fileName = System.IO.Path.GetFileName(Path);
            return fileName.Length > maxLength ? fileName.Substring(0, maxLength - 3) + "..." : fileName;
        }

        return $"{Path.Substring(0, startLength)}...{Path.Substring(Path.Length - endLength)}";
    }

    /// <summary>
    /// 获取版本-分支显示
    /// </summary>
    public string GetVersionBranchDisplay()
    {
        if (string.IsNullOrEmpty(Version) && string.IsNullOrEmpty(Branch))
            return "未知版本";
        
        if (string.IsNullOrEmpty(Version))
            return Branch;
        
        if (string.IsNullOrEmpty(Branch))
            return Version;
        
        return $"{Version}-{Branch}";
    }

    /// <summary>
    /// 获取构建日期-时间显示
    /// </summary>
    public string GetBuildDateTimeDisplay()
    {
        if (BuildDate.HasValue && !string.IsNullOrEmpty(BuildTime))
        {
            return $"{BuildDate.Value:yyyy-MM-dd}-{BuildTime}";
        }
        
        if (BuildDate.HasValue)
        {
            return BuildDate.Value.ToString("yyyy-MM-dd");
        }
        
        if (!string.IsNullOrEmpty(BuildTime))
        {
            return BuildTime;
        }
        
        return "未知构建时间";
    }

    /// <summary>
    /// 检查Blender可执行文件是否仍然有效
    /// </summary>
    public bool IsFileStillValid()
    {
        return !string.IsNullOrEmpty(Path) && System.IO.File.Exists(Path);
    }

    /// <summary>
    /// 更新验证状态
    /// </summary>
    public void UpdateValidationStatus(bool isValid, DateTime validatedAt)
    {
        IsValid = isValid;
        LastValidated = validatedAt;
    }

    /// <summary>
    /// 从BlenderVersionInfo更新信息
    /// </summary>
    public void UpdateFromVersionInfo(BlenderRenderQueue.Services.BlenderService.BlenderVersionInfo versionInfo)
    {
        Version = versionInfo.Version ?? string.Empty;
        Platform = versionInfo.Platform ?? string.Empty;
        Branch = versionInfo.Branch ?? string.Empty;
        Hash = versionInfo.Hash ?? string.Empty;
        BuildDate = versionInfo.BuildDate;
        BuildTime = versionInfo.BuildTime ?? string.Empty;
        CommitDate = versionInfo.CommitDate;
        CommitTime = versionInfo.CommitTime ?? string.Empty;
        Type = versionInfo.Type ?? string.Empty;
    }

    /// <summary>
    /// 创建默认的BlenderExecutable实例
    /// </summary>
    public static BlenderExecutable CreateDefault(string path)
    {
        return new BlenderExecutable
        {
            Path = path,
            IsValid = false,
            LastValidated = DateTime.UtcNow
        };
    }
}
