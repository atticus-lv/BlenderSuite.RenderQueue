using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlenderRenderQueue.Models;

/// <summary>
/// Settings Data model - Store independently to settings.json
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
    public int DefaultRenderTimeoutSeconds { get; set; } = 300; // Default five minutes

    [JsonPropertyName("MaxRetryAttempts")]
    public int MaxRetryAttempts { get; set; } = 3;

    [JsonPropertyName("VideoCodec")]
    public string VideoCodec { get; set; } = "H264";

    [JsonPropertyName("VideoQuality")]
    public string VideoQuality { get; set; } = "PERC_LOSSLESS";

    [JsonPropertyName("Language")]
    public string Language { get; set; } = "en-US";

    [JsonPropertyName("BaseTheme")]
    public string BaseTheme { get; set; } = "Dark";

    [JsonPropertyName("Vulkan")]
    public bool Vulkan { get; set; } = true;


    public BlenderExecutable? GetSelectedBlender()
    {
        return string.IsNullOrEmpty(SelectedBlenderPath)
            ? null
            : BlenderExecutables.Find(b => b.Path == SelectedBlenderPath);
    }


    public void AddOrUpdateBlender(BlenderExecutable blender)
    {
        var existing = BlenderExecutables.Find(b => b.Path == blender.Path);
        if (existing != null)
        {
            var index = BlenderExecutables.IndexOf(existing);
            BlenderExecutables[index] = blender;
        }
        else
        {
            BlenderExecutables.Add(blender);
        }
    }


    public bool RemoveBlender(string path)
    {
        var blender = BlenderExecutables.Find(b => b.Path == path);
        if (blender == null) return false;
        BlenderExecutables.Remove(blender);

        if (SelectedBlenderPath == path)
        {
            SelectedBlenderPath = string.Empty;
        }

        return true;
    }


    public void CleanupInvalidBlenders()
    {
        BlenderExecutables.RemoveAll(b => !b.IsFileStillValid());

        if (string.IsNullOrEmpty(SelectedBlenderPath)) return;
        var selected = GetSelectedBlender();
        if (selected == null || !selected.IsFileStillValid())
        {
            SelectedBlenderPath = string.Empty;
        }
    }
}