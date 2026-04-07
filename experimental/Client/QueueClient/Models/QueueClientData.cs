using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlenderSuite.RenderQueue.Models;

/// <summary>
/// 队列客户端数据模型
/// </summary>
public class QueueClientData
{
    /// <summary>
    /// 服务器URL列表
    /// </summary>
    [JsonPropertyName("serverUrls")]
    public List<string> ServerUrls { get; set; } = new();

    /// <summary>
    /// 当前选中的服务器URL
    /// </summary>
    [JsonPropertyName("selectedServerUrl")]
    public string SelectedServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// 刷新间隔（秒）
    /// </summary>
    [JsonPropertyName("refreshInterval")]
    public int RefreshInterval { get; set; } = 1;

    /// <summary>
    /// 是否自动刷新
    /// </summary>
    [JsonPropertyName("autoRefresh")]
    public bool AutoRefresh { get; set; } = true;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// 软件标识
    /// </summary>
    [JsonPropertyName("software")]
    public string Software { get; set; } = "QueueClient";

    /// <summary>
    /// 版本号
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
}
