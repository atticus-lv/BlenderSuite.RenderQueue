using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Models;

/// <summary>
/// 设置数据模型 - 独立存储到 settings.json
/// </summary>
public class SettingsData
{
    [JsonPropertyName("Software")]
    public string Software { get; set; } = "BlenderRenderQueue";

    [JsonPropertyName("Version")]
    public string Version { get; set; } = "0.0.1";

    [JsonPropertyName("BlenderExecutables")]
    public List<BlenderExecutable> BlenderExecutables { get; set; } = new();

    [JsonPropertyName("SelectedBlenderPath")]
    public string SelectedBlenderPath { get; set; } = string.Empty;

    [JsonPropertyName("DefaultRenderTimeoutSeconds")]
    public int DefaultRenderTimeoutSeconds { get; set; } = 300; // 默认五分钟

    [JsonPropertyName("MaxRetryAttempts")]
    public int MaxRetryAttempts { get; set; } = 3; // 默认最大重试3次

    [JsonPropertyName("VideoCodec")]
    public string VideoCodec { get; set; } = "H264"; // 默认使用H264编码

    [JsonPropertyName("VideoQuality")]
    public string VideoQuality { get; set; } = "PERC_LOSSLESS"; // 默认感知无损质量

    [JsonPropertyName("Language")]
    public string Language { get; set; } = "en-US"; // 默认英语

    [JsonPropertyName("BaseTheme")]
    public string BaseTheme { get; set; } = "Dark"; // 默认深色主题

    /// <summary>
    /// 获取选中的Blender可执行文件
    /// </summary>
    public BlenderExecutable? GetSelectedBlender()
    {
        if (string.IsNullOrEmpty(SelectedBlenderPath))
            return null;

        return BlenderExecutables.Find(b => b.Path == SelectedBlenderPath);
    }

    /// <summary>
    /// 添加或更新Blender可执行文件
    /// </summary>
    public void AddOrUpdateBlender(BlenderExecutable blender)
    {
        var existing = BlenderExecutables.Find(b => b.Path == blender.Path);
        if (existing != null)
        {
            // 更新现有项
            var index = BlenderExecutables.IndexOf(existing);
            BlenderExecutables[index] = blender;
        }
        else
        {
            // 添加新项
            BlenderExecutables.Add(blender);
        }
    }

    /// <summary>
    /// 移除Blender可执行文件
    /// </summary>
    public bool RemoveBlender(string path)
    {
        var blender = BlenderExecutables.Find(b => b.Path == path);
        if (blender != null)
        {
            BlenderExecutables.Remove(blender);
            
            // 如果移除的是当前选中的，清空选择
            if (SelectedBlenderPath == path)
            {
                SelectedBlenderPath = string.Empty;
            }
            
            return true;
        }
        return false;
    }

    /// <summary>
    /// 清理无效的Blender可执行文件
    /// </summary>
    public void CleanupInvalidBlenders()
    {
        BlenderExecutables.RemoveAll(b => !b.IsFileStillValid());
        
        // 如果当前选中的Blender无效，清空选择
        if (!string.IsNullOrEmpty(SelectedBlenderPath))
        {
            var selected = GetSelectedBlender();
            if (selected == null || !selected.IsFileStillValid())
            {
                SelectedBlenderPath = string.Empty;
            }
        }
    }
}
